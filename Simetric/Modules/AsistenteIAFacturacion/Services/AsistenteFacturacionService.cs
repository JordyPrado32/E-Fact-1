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
            OpcionesSeleccion = state.SeleccionPendiente?.Opciones ?? new List<SelectionOptionDto>(),
            Progreso = BuildProgress(state, result.AccionDetectada, result.Respuesta),
            DatosFaltantes = BuildMissingData(state, result.Respuesta)
        };
    }

    private static List<BotProgressStepDto> BuildProgress(FacturaConversationState state, string? action, string response)
    {
        var normalizedAction = action?.Trim().ToLowerInvariant() ?? state.UltimaIntencion?.Trim().ToLowerInvariant();
        var steps = normalizedAction switch
        {
            "crear_cliente" => new List<BotProgressStepDto>
            {
                Step("interpretar", "Entendiendo los datos del cliente", "Analizando nombre, identificación y contacto.", "completed"),
                Step("buscar_cliente", "Buscando coincidencias", "Comparando con clientes existentes para evitar duplicados.", "completed"),
                Step("validar_identificacion", "Validando identificación", response.Contains("no encontr", StringComparison.OrdinalIgnoreCase) ? "No encontré una coincidencia con esa identificación." : "Revisando el formato y la disponibilidad.", response.Contains("no encontr", StringComparison.OrdinalIgnoreCase) ? "warning" : "completed"),
                Step("crear_cliente", "Preparando registro", "Solo se solicitarán los datos obligatorios que falten.", "pending")
            },
            "crear_producto" => new List<BotProgressStepDto>
            {
                Step("interpretar", "Entendiendo los datos del producto", "Analizando nombre, código, precio e IVA.", "completed"),
                Step("buscar_producto", "Buscando coincidencias", "Revisando el catálogo para evitar duplicados.", "completed"),
                Step("validar_producto", "Validando precio e IVA", "Comprobando los datos necesarios para facturar.", "completed"),
                Step("crear_producto", "Preparando registro", "Solo se solicitarán los datos obligatorios que falten.", "pending")
            },
            "agregar_item" or "producto_seleccionado" => new List<BotProgressStepDto>
            {
                Step("buscar_producto", "Buscando producto", "Comparando nombre y código en tu catálogo.", "completed"),
                Step("validar_item", "Validando precio e IVA", "Usando la configuración real del producto.", "completed"),
                Step("calcular", "Calculando línea", "Actualizando cantidad, descuento y total.", "completed")
            },
            "crear_factura" or "preparar_emision" or "validar_factura" => new List<BotProgressStepDto>
            {
                Step("cliente", "Buscando cliente", state.Draft.Cliente is null ? "Falta seleccionar un cliente." : $"Cliente encontrado: {state.Draft.Cliente.Nombre}.", state.Draft.Cliente is null ? "warning" : "completed"),
                Step("items", "Revisando productos", state.Draft.Items.Count == 0 ? "Falta agregar al menos un producto o servicio." : $"{state.Draft.Items.Count} producto(s) listos para revisar.", state.Draft.Items.Count == 0 ? "warning" : "completed"),
                Step("totales", "Calculando factura", $"Total actual: ${state.Draft.Total:0.00}.", "completed"),
                Step("confirmacion", "Esperando confirmación", "No se emitirá nada sin tu autorización explícita.", state.RequiereConfirmacion ? "pending" : "completed")
            },
            "consultar_facturas" => new List<BotProgressStepDto>
            {
                Step("buscar", "Buscando comprobantes", "Consultando facturas reales de tu cuenta.", "completed"),
                Step("resumir", "Preparando resumen", "Ordenando resultados y estados.", "completed")
            },
            _ => new List<BotProgressStepDto>
            {
                Step("interpretar", "Entendiendo tu solicitud", "Identificando la acción que necesitas.", "completed"),
                Step("consultar", "Consultando la información", "Revisando el contexto de tu conversación.", "completed"),
                Step("responder", "Preparando respuesta", "Tengo el siguiente paso listo para ti.", "completed")
            }
        };

        return steps;
    }

    private static List<string> BuildMissingData(FacturaConversationState state, string response)
    {
        var missing = new List<string>();
        if (state.UltimaIntencion is "crear_factura" or "preparar_emision" or "validar_factura")
        {
            if (state.Draft.Cliente is null) missing.Add("cliente");
            if (state.Draft.Items.Count == 0) missing.Add("producto o servicio");
            if (state.RequiereConfirmacion) missing.Add("confirmación para emitir");
        }

        foreach (var field in new[] { "nombre", "identificación", "cedula", "cédula", "correo", "dirección", "precio", "cantidad" })
        {
            if (response.Contains($"falta {field}", StringComparison.OrdinalIgnoreCase) || response.Contains($"faltan {field}", StringComparison.OrdinalIgnoreCase))
                missing.Add(field);
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static BotProgressStepDto Step(string id, string label, string detail, string status)
        => new() { Id = id, Label = label, Detail = detail, Status = status };

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
