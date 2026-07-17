using System.Globalization;
using System.Text;
using System.Text.Json;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.State;
using Simetric.Modules.AsistenteIAFacturacion.Tools;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public sealed class AsistenteFacturacionService : IAsistenteFacturacionService
{
    private readonly IFacturaConversationStore _conversationStore;
    private readonly IOpenAIAsistenteService _openAIAsistenteService;

    public AsistenteFacturacionService(
        IFacturaConversationStore conversationStore,
        IOpenAIAsistenteService openAIAsistenteService)
    {
        _conversationStore = conversationStore;
        _openAIAsistenteService = openAIAsistenteService;
    }

    public async Task<ChatFacturaResponse> ProcesarAsync(int userId, ChatFacturaRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            request.SessionId = Guid.NewGuid().ToString("N");

        var state = await _conversationStore.GetOrCreateAsync(userId, request.SessionId, cancellationToken);
        state.UserId = userId;
        state.SessionId = request.SessionId;
        state.ActualizadoEn = DateTimeOffset.UtcNow;
        var mensaje = request.Mensaje.Trim();

        state.Historial.Add(new FacturaConversationMessage
        {
            Role = "user",
            Content = mensaje
        });
        TrimHistorial(state);

        var pendingSelectionResponse = await TryResolvePendingSelectionWithContinuationAsync(state, mensaje, cancellationToken);
        if (pendingSelectionResponse is not null)
        {
            state.UltimaAccionEstructurada = BuildStructuredActionSnapshot(state);
            state.Historial.Add(new FacturaConversationMessage
            {
                Role = "assistant",
                Content = pendingSelectionResponse.Respuesta
            });

            await _conversationStore.SaveAsync(state, cancellationToken);
            return pendingSelectionResponse;
        }

        var fastPathResult = await _openAIAsistenteService.TryProcesarRapidoAsync(state, mensaje, cancellationToken);
        var result = fastPathResult ?? await _openAIAsistenteService.ProcesarAsync(state, mensaje, cancellationToken);
        state.UltimaAccionEstructurada = BuildStructuredActionSnapshot(state);
        state.Historial.Add(new FacturaConversationMessage
        {
            Role = "assistant",
            Content = result.Respuesta
        });
        TrimHistorial(state);

        await _conversationStore.SaveAsync(state, cancellationToken);

        return new ChatFacturaResponse
        {
            SessionId = state.SessionId,
            Respuesta = result.Respuesta,
            Estado = state.Estado,
            FacturaDraft = state.Draft,
            RequiereConfirmacion = state.RequiereConfirmacion,
            Emitida = state.Emitida,
            AccionDetectada = result.AccionDetectada ?? state.UltimaIntencion,
            RutaSugerida = result.RutaSugerida,
            SeleccionPendienteTipo = state.SeleccionPendiente?.Tipo,
            SeleccionPendienteMensaje = state.SeleccionPendiente?.Mensaje,
            OpcionesSeleccion = state.SeleccionPendiente?.Opciones ?? new List<SelectionOptionDto>()
        };
    }

    private static Task<ChatFacturaResponse?> TryResolvePendingSelectionAsync(
        FacturaConversationState state,
        string mensaje,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (state.SeleccionPendiente is null || state.SeleccionPendiente.Opciones.Count == 0)
            return Task.FromResult<ChatFacturaResponse?>(null);

        var opcion = ResolvePendingOption(state.SeleccionPendiente, mensaje);
        if (opcion is null)
        {
            return Task.FromResult<ChatFacturaResponse?>(new ChatFacturaResponse
            {
                SessionId = state.SessionId,
                Respuesta = $"{state.SeleccionPendiente.Mensaje} Puedes responder con el numero, nombre o identificacion.",
                Estado = state.Estado,
                FacturaDraft = state.Draft,
                RequiereConfirmacion = state.RequiereConfirmacion,
                Emitida = state.Emitida,
                AccionDetectada = "seleccion_pendiente",
                SeleccionPendienteTipo = state.SeleccionPendiente.Tipo,
                SeleccionPendienteMensaje = state.SeleccionPendiente.Mensaje,
                OpcionesSeleccion = state.SeleccionPendiente.Opciones
            });
        }

        var response = ApplyPendingOption(state, opcion);
        state.SeleccionPendiente = null;
        return Task.FromResult<ChatFacturaResponse?>(response);
    }

    private static SelectionOptionDto? ResolvePendingOption(PendingSelectionState pending, string mensaje)
    {
        var texto = mensaje.Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (int.TryParse(new string(texto.Where(char.IsDigit).ToArray()), out var indice))
            return pending.Opciones.FirstOrDefault(x => x.Indice == indice);

        return pending.Opciones.FirstOrDefault(x => MatchesPendingOption(x, texto));
    }

    private async Task<ChatFacturaResponse?> TryResolvePendingSelectionWithContinuationAsync(
        FacturaConversationState state,
        string mensaje,
        CancellationToken cancellationToken)
    {
        if (state.SeleccionPendiente is null || state.SeleccionPendiente.Opciones.Count == 0)
            return null;

        var pending = state.SeleccionPendiente;
        var response = await TryResolvePendingSelectionAsync(state, mensaje, cancellationToken);
        if (response is null || pending is null)
            return response;

        if (!string.Equals(pending.Accion, "registrar_abono", StringComparison.OrdinalIgnoreCase))
            return response;

        var montoPendiente = pending.Monto;
        if (response.AccionDetectada == "seleccion_pendiente" || !montoPendiente.HasValue || montoPendiente.Value <= 0m || state.Draft.Cliente is null)
            return response;

        var mensajeSintetico = $"registra un abono de {montoPendiente.Value:0.00} para {state.Draft.Cliente.Nombre}";
        var autoResultado = await _openAIAsistenteService.TryProcesarRapidoAsync(state, mensajeSintetico, cancellationToken)
            ?? await _openAIAsistenteService.ProcesarAsync(state, mensajeSintetico, cancellationToken);

        return new ChatFacturaResponse
        {
            SessionId = state.SessionId,
            Respuesta = autoResultado.Respuesta,
            Estado = state.Estado,
            FacturaDraft = state.Draft,
            RequiereConfirmacion = state.RequiereConfirmacion,
            Emitida = state.Emitida,
            AccionDetectada = autoResultado.AccionDetectada ?? state.UltimaIntencion,
            SeleccionPendienteTipo = state.SeleccionPendiente?.Tipo,
            SeleccionPendienteMensaje = state.SeleccionPendiente?.Mensaje,
            OpcionesSeleccion = state.SeleccionPendiente?.Opciones ?? new List<SelectionOptionDto>()
        };
    }

    private static ChatFacturaResponse ApplyPendingOption(FacturaConversationState state, SelectionOptionDto opcion)
    {
        if (string.Equals(opcion.Tipo, "cliente", StringComparison.OrdinalIgnoreCase) && opcion.Cliente is not null)
        {
            state.Draft.Cliente = opcion.Cliente;
            state.Estado = FacturaConversationStates.ClienteSeleccionado;

            return new ChatFacturaResponse
            {
                SessionId = state.SessionId,
                Respuesta = $"Cliente seleccionado: {opcion.Cliente.Nombre}. Ya puedes seguir agregando productos o pedir el resumen.",
                Estado = state.Estado,
                FacturaDraft = state.Draft,
                RequiereConfirmacion = state.RequiereConfirmacion,
                Emitida = state.Emitida,
                AccionDetectada = "cliente_seleccionado"
            };
        }

        if (string.Equals(opcion.Tipo, "producto", StringComparison.OrdinalIgnoreCase) && opcion.Producto is not null)
        {
            var cantidad = state.SeleccionPendiente?.Cantidad ?? 1m;
            var existing = state.Draft.Items.FirstOrDefault(x => x.ProductoId == opcion.Producto.Id && !x.EsServicioManual);
            if (existing is null)
            {
                existing = new FacturaItemDraftDto
                {
                    ProductoId = opcion.Producto.Id,
                    Descripcion = opcion.Producto.Nombre,
                    CodigoPrincipal = opcion.Producto.CodigoPrincipal,
                    Cantidad = cantidad,
                    PrecioUnitario = opcion.Producto.PrecioUnitario,
                    DescuentoPorcentaje = state.SeleccionPendiente?.DescuentoPorcentaje,
                    DescuentoValor = state.SeleccionPendiente?.DescuentoValor,
                    TarifaPorcentaje = opcion.Producto.TarifaPorcentaje,
                    EsServicioManual = false
                };
                state.Draft.Items.Add(existing);
            }
            else
            {
                existing.Cantidad += cantidad;
            }

            state.Estado = FacturaConversationStates.AgregandoItems;
            state.UltimaIntencion = "agregar_item";
            FacturacionTools.Recalculate(state.Draft);

            return new ChatFacturaResponse
            {
                SessionId = state.SessionId,
                Respuesta = $"Producto seleccionado: {opcion.Producto.Nombre}. Lo agregue al borrador con cantidad {cantidad:0.##}.",
                Estado = state.Estado,
                FacturaDraft = state.Draft,
                RequiereConfirmacion = state.RequiereConfirmacion,
                Emitida = state.Emitida,
                AccionDetectada = "producto_seleccionado"
            };
        }

        return new ChatFacturaResponse
        {
            SessionId = state.SessionId,
            Respuesta = "No pude aplicar la seleccion indicada.",
            Estado = state.Estado,
            FacturaDraft = state.Draft,
            RequiereConfirmacion = state.RequiereConfirmacion,
            Emitida = state.Emitida,
            AccionDetectada = "seleccion_invalida"
        };
    }

    private static string BuildStructuredActionSnapshot(FacturaConversationState state)
    {
        var action = new
        {
            intencion = state.UltimaIntencion,
            cliente = state.Draft.Cliente?.Nombre,
            items = state.Draft.Items.Select(x => new
            {
                descripcion = x.Descripcion,
                cantidad = x.Cantidad,
                descuentoPorcentaje = x.DescuentoPorcentaje,
                descuentoValor = x.DescuentoValor
            }),
            formaPago = state.Draft.FormaPago,
            diasCredito = state.Draft.DiasCredito,
            fechaVencimiento = state.Draft.FechaVencimiento,
            requiereConfirmacion = state.RequiereConfirmacion
        };

        return JsonSerializer.Serialize(action);
    }

    private static void TrimHistorial(FacturaConversationState state)
    {
        const int maxMessages = 24;
        if (state.Historial.Count <= maxMessages)
            return;

        state.Historial = state.Historial
            .TakeLast(maxMessages)
            .ToList();
    }

    private static bool MatchesPendingOption(SelectionOptionDto option, string texto)
    {
        var normalizedInput = NormalizeForMatch(texto);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return false;

        var candidates = new[]
        {
            option.Etiqueta,
            option.Descripcion,
            option.Cliente?.Nombre,
            option.Cliente?.Identificacion,
            option.Producto?.Nombre,
            option.Producto?.CodigoPrincipal
        };

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeForMatch(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
                continue;

            if (normalizedCandidate == normalizedInput ||
                normalizedCandidate.Contains(normalizedInput, StringComparison.Ordinal) ||
                normalizedInput.Contains(normalizedCandidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var inputTokens = normalizedInput
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (inputTokens.Length == 0)
            return false;

        return candidates
            .Select(NormalizeForMatch)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Any(candidate => inputTokens.All(token => candidate.Contains(token, StringComparison.Ordinal)));
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
        }

        return string.Join(" ",
            builder.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
