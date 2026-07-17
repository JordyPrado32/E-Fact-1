using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetric.Modules.AsistenteIAFacturacion.Config;
using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.Modules.AsistenteIAFacturacion.Prompts;
using Simetric.Modules.AsistenteIAFacturacion.State;
using Simetric.Modules.AsistenteIAFacturacion.Tools;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public sealed class OpenAIAsistenteService : IOpenAIAsistenteService
{
    private readonly HttpClient _httpClient;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly OpenAISettings _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIAsistenteService> _logger;

    public OpenAIAsistenteService(
        HttpClient httpClient,
        ToolDispatcher toolDispatcher,
        IOptions<OpenAISettings> settings,
        IConfiguration configuration,
        ILogger<OpenAIAsistenteService> logger)
    {
        _httpClient = httpClient;
        _toolDispatcher = toolDispatcher;
        _settings = settings.Value;
        _configuration = configuration;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
            _httpClient.BaseAddress = new Uri(ResolveBaseUrl());
    }

    public async Task<OpenAIAsistenteResult> ProcesarAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken = default)
    {
        var diagnostics = GetDiagnostics();
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "Asistente IA sin ApiKey configurada. Source={ApiKeySource} Model={Model} BaseUrl={BaseUrl}",
                diagnostics.ApiKeySource,
                diagnostics.Model,
                diagnostics.BaseUrl);

            return new OpenAIAsistenteResult
            {
                Respuesta = $"La integracion con OpenAI no esta configurada en este entorno. Debes definir OPENAI_API_KEY o OpenAI__ApiKey en el servidor. Modelo actual: {diagnostics.Model}.",
                AccionDetectada = "configuracion_pendiente"
            };
        }

        try
        {
            return await ProcesarConOpenAIAsync(state, mensaje, apiKey, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Error HTTP al invocar OpenAI. Source={ApiKeySource} Model={Model} BaseUrl={BaseUrl}",
                diagnostics.ApiKeySource,
                diagnostics.Model,
                diagnostics.BaseUrl);

            return new OpenAIAsistenteResult
            {
                Respuesta = $"No pude comunicarme correctamente con OpenAI. {ex.Message}",
                AccionDetectada = "error_openai"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo OpenAIAsistenteService. Se usara el parser local.");
            return await ProcesarConFallbackAsync(state, mensaje, cancellationToken);
        }
    }

    public Task<OpenAIAsistenteResult?> TryProcesarRapidoAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken = default)
    {
        var normalized = mensaje.Trim().ToLowerInvariant();
        if (!ShouldUseLocalFastPath(state, normalized))
            return Task.FromResult<OpenAIAsistenteResult?>(null);

        return TryProcesarRapidoCoreAsync(state, mensaje, cancellationToken);
    }

    public OpenAIDiagnosticsDto GetDiagnostics()
    {
        var apiKey = ResolveApiKey();
        return new OpenAIDiagnosticsDto
        {
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeySource = ResolveApiKeySource(),
            Model = ResolveModel(),
            BaseUrl = ResolveBaseUrl()
        };
    }

    private async Task<OpenAIAsistenteResult> ProcesarConOpenAIAsync(FacturaConversationState state, string mensaje, string apiKey, CancellationToken cancellationToken)
    {
        var messages = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = SystemPromptFacturacion.Build(state)
            }
        };

        foreach (var historyItem in state.Historial.TakeLast(10))
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = historyItem.Role,
                ["content"] = ClipForModel(historyItem.Content)
            });
        }

        messages.Add(new Dictionary<string, object?>
        {
            ["role"] = "user",
            ["content"] = ClipForModel(mensaje)
        });

        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model = ResolveModel(),
                    messages,
                    tools = ToolDefinitions.BuildTools(),
                    tool_choice = "auto",
                    temperature = 0.05
                }),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"OpenAI devolvio {(int)response.StatusCode}: {body}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var message = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");

            var content = ExtractContent(message);
            var toolCalls = ParseToolCalls(message);

            if (toolCalls.Count == 0)
            {
                return new OpenAIAsistenteResult
                {
                    Respuesta = string.IsNullOrWhiteSpace(content)
                        ? "Tengo el borrador listo. Deseas que continue con la factura?"
                        : content,
                    AccionDetectada = state.UltimaIntencion
                };
            }

            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = content ?? string.Empty,
                ["tool_calls"] = toolCalls.Select(x => new
                {
                    id = x.Id,
                    type = "function",
                    function = new
                    {
                        name = x.Name,
                        arguments = x.Arguments
                    }
                }).ToArray()
            });

            foreach (var toolCall in toolCalls)
            {
                var toolResult = await _toolDispatcher.DispatchAsync(toolCall.Name, toolCall.Arguments, state, cancellationToken);
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = toolCall.Id,
                    ["name"] = toolCall.Name,
                    ["content"] = JsonSerializer.Serialize(toolResult)
                });
            }
        }

        return new OpenAIAsistenteResult
        {
            Respuesta = "Prepare el borrador pero necesito que revises los datos antes de continuar.",
            AccionDetectada = state.UltimaIntencion
        };
    }

    private async Task<OpenAIAsistenteResult> ProcesarConFallbackAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken)
    {
        var normalized = mensaje.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "cancela", "cancelar", "me equivoque", "no"))
        {
            state.Estado = FacturaConversationStates.Cancelado;
            state.RequiereConfirmacion = false;
            return new OpenAIAsistenteResult
            {
                Respuesta = "Cancele la operacion actual. Si quieres, puedo empezar una nueva factura.",
                AccionDetectada = "cancelar"
            };
        }

        if (state.Estado == FacturaConversationStates.EsperandoConfirmacion &&
            ContainsAny(normalized, "si", "confirmo", "dale", "correcto", "emit"))
        {
            var result = await _toolDispatcher.DispatchAsync(ToolDefinitions.EmitirFactura, "{}", state, cancellationToken);
            return new OpenAIAsistenteResult
            {
                Respuesta = result.Message,
                AccionDetectada = "confirmar_emision"
            };
        }

        var notaCreditoResult = await TryHandleNotaCreditoCommandAsync(state, mensaje, normalized, cancellationToken);
        if (notaCreditoResult is not null)
            return notaCreditoResult;

        var notaCreditoRoute = ResolveNotaCreditoRoute(normalized);
        if (!string.IsNullOrWhiteSpace(notaCreditoRoute))
        {
            state.RequiereConfirmacion = false;
            return new OpenAIAsistenteResult
            {
                Respuesta = notaCreditoRoute == "/facturacion/notas-credito-generadas"
                    ? "Te llevo al historial de notas de crédito para que revises las ya emitidas."
                    : "Te llevo al módulo de nota de crédito para que trabajes ese documento desde su flujo correcto.",
                AccionDetectada = "ir_nota_credito",
                RutaSugerida = notaCreditoRoute
            };
        }

        var quickCollectionResult = await TryHandleCollectionCommandAsync(state, mensaje, normalized, cancellationToken);
        if (quickCollectionResult is not null)
            return quickCollectionResult;

        var quickPaymentResult = await TryHandlePaymentMethodCommandAsync(state, normalized, cancellationToken);
        if (quickPaymentResult is not null)
            return quickPaymentResult;

        if (state.Draft.Cliente is not null && state.Draft.Items.Count > 0 && ContainsAny(normalized, "emitir", "emite", "finaliza", "finalizar"))
        {
            var validation = await _toolDispatcher.DispatchAsync(ToolDefinitions.ValidarFactura, "{}", state, cancellationToken);
            if (!validation.Success)
            {
                return new OpenAIAsistenteResult
                {
                    Respuesta = validation.Message,
                    AccionDetectada = "validar_factura"
                };
            }

            var summary = await _toolDispatcher.DispatchAsync(ToolDefinitions.ObtenerResumenFactura, "{}", state, cancellationToken);
            return new OpenAIAsistenteResult
            {
                Respuesta = $"{summary.Message}\nConfirma una sola vez para emitir la factura.",
                AccionDetectada = "preparar_emision"
            };
        }

        var draftCommandResult = await TryHandleDraftVoiceCommandAsync(state, mensaje, normalized, cancellationToken);
        if (draftCommandResult is not null)
            return draftCommandResult;

        if (ContainsAny(normalized, "resumen", "como va", "muestra la factura"))
        {
            var result = await _toolDispatcher.DispatchAsync(ToolDefinitions.ObtenerResumenFactura, "{}", state, cancellationToken);
            return new OpenAIAsistenteResult
            {
                Respuesta = result.Message,
                AccionDetectada = "consultar_resumen"
            };
        }

        if (ContainsAny(normalized, "crea", "crear", "haz", "genera", "factura", "emite"))
        {
            await _toolDispatcher.DispatchAsync(ToolDefinitions.CrearBorradorFactura, "{}", state, cancellationToken);

            var clientMatch = Regex.Match(
                mensaje,
                @"(?:a|para)\s+(?<cliente>[\p{L}0-9][\p{L}0-9\s\.\-']+?)(?=\s+de\s+|\s+con\s+|\s+a\s+cr[eé]dito|\s+al?\s+contado|$)",
                RegexOptions.IgnoreCase);
            if (clientMatch.Success)
            {
                var clienteNombre = clientMatch.Groups["cliente"].Value.Trim();
                var clientResult = await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.BuscarCliente,
                    JsonSerializer.Serialize(new { query = clienteNombre }),
                    state,
                    cancellationToken);

                if (clientResult.Data is IReadOnlyList<ClienteDto> clientes && clientes.Count == 1)
                {
                    await _toolDispatcher.DispatchAsync(
                        ToolDefinitions.CrearBorradorFactura,
                        JsonSerializer.Serialize(new { clienteId = clientes[0].Id }),
                        state,
                        cancellationToken);
                }
                else if (clientResult.Data is IReadOnlyList<ClienteDto> multiplesClientes && multiplesClientes.Count > 1)
                {
                    state.SeleccionPendiente = new PendingSelectionState
                    {
                        Tipo = "cliente",
                        Mensaje = $"Encontré varios clientes para '{clienteNombre}'. Elige uno:",
                        Opciones = multiplesClientes.Take(5).Select((cliente, index) => new SelectionOptionDto
                        {
                            Indice = index + 1,
                            Tipo = "cliente",
                            Etiqueta = cliente.Nombre,
                            Descripcion = $"{cliente.Identificacion ?? "Sin identificación"}{(string.IsNullOrWhiteSpace(cliente.NumeroNotificacion) ? string.Empty : $" | Notif.: {cliente.NumeroNotificacion}")}",
                            Cliente = cliente
                        }).ToList()
                    };

                    return new OpenAIAsistenteResult
                    {
                        Respuesta = state.SeleccionPendiente.Mensaje,
                        AccionDetectada = "seleccion_cliente"
                    };
                }
            }

            var formaPagoDetectada = DetectPaymentMethod(normalized);
            if (!string.IsNullOrWhiteSpace(formaPagoDetectada))
            {
                var diasCredito = ParseCreditDays(normalized);
                await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.ModificarFormaPago,
                    JsonSerializer.Serialize(new { formaPago = formaPagoDetectada, diasCredito }),
                    state,
                    cancellationToken);
            }

            foreach (Match itemMatch in Regex.Matches(
                mensaje,
                @"(?<cantidad>\d+(?:[.,]\d+)?|un|una|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez)\s+(?<producto>[\p{L}][\p{L}0-9\s\.\-]+?)(?=,| y | con | a\s+cr[eé]dito| al?\s+contado|$)",
                RegexOptions.IgnoreCase))
            {
                var productoQuery = itemMatch.Groups["producto"].Value.Trim();
                var cantidad = ParseAmountToken(itemMatch.Groups["cantidad"].Value) ?? 0m;
                var productSearch = await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.BuscarProducto,
                    JsonSerializer.Serialize(new { query = productoQuery }),
                    state,
                    cancellationToken);

                if (productSearch.Data is IReadOnlyList<ProductoDto> productos && productos.Count == 1)
                {
                    decimal? descuentoPct = null;
                    var discountMatch = Regex.Match(
                        mensaje,
                        @"(?<descuento>\d+(?:[.,]\d+)?)\s*%\s*de\s*descuento",
                        RegexOptions.IgnoreCase);
                    if (discountMatch.Success)
                        descuentoPct = ParseDecimal(discountMatch.Groups["descuento"].Value);

                    await _toolDispatcher.DispatchAsync(
                        ToolDefinitions.AgregarProductoAFactura,
                        JsonSerializer.Serialize(new
                        {
                            productoId = productos[0].Id,
                            cantidad,
                            descuentoPorcentaje = descuentoPct
                        }),
                        state,
                        cancellationToken);
                }
                else if (productSearch.Data is IReadOnlyList<ProductoDto> multiplesProductos && multiplesProductos.Count > 1)
                {
                    decimal? descuentoPct = null;
                    var discountMatch = Regex.Match(
                        mensaje,
                        @"(?<descuento>\d+(?:[.,]\d+)?)\s*%\s*de\s*descuento",
                        RegexOptions.IgnoreCase);
                    if (discountMatch.Success)
                        descuentoPct = ParseDecimal(discountMatch.Groups["descuento"].Value);

                    state.SeleccionPendiente = new PendingSelectionState
                    {
                        Tipo = "producto",
                        Mensaje = $"Encontré varios productos para '{productoQuery}'. Elige uno:",
                        Cantidad = cantidad,
                        DescuentoPorcentaje = descuentoPct,
                        Opciones = multiplesProductos.Take(5).Select((producto, index) => new SelectionOptionDto
                        {
                            Indice = index + 1,
                            Tipo = "producto",
                            Etiqueta = producto.Nombre,
                            Descripcion = $"{producto.CodigoPrincipal ?? "Sin código"} | {(producto.Categoria ?? producto.Tipo ?? "Sin categoría")}{(string.IsNullOrWhiteSpace(producto.Subcategoria) ? string.Empty : $" / {producto.Subcategoria}")} | ${producto.PrecioUnitario:0.00}",
                            Producto = producto
                        }).ToList()
                    };

                    return new OpenAIAsistenteResult
                    {
                        Respuesta = state.SeleccionPendiente.Mensaje,
                        AccionDetectada = "seleccion_producto"
                    };
                }
            }

            await _toolDispatcher.DispatchAsync(ToolDefinitions.CalcularTotales, "{}", state, cancellationToken);
            var validation = await _toolDispatcher.DispatchAsync(ToolDefinitions.ValidarFactura, "{}", state, cancellationToken);
            var summary = await _toolDispatcher.DispatchAsync(ToolDefinitions.ObtenerResumenFactura, "{}", state, cancellationToken);

            return new OpenAIAsistenteResult
            {
                Respuesta = validation.Success
                    ? $"{summary.Message}\nDeseas emitir la factura?"
                    : $"Empece el borrador. {validation.Message}",
                AccionDetectada = "crear_factura"
            };
        }

        return new OpenAIAsistenteResult
        {
            Respuesta = "Puedo ayudarte a crear una factura. Por ejemplo: 'Haz una factura a Juan Perez de 2 teclados con 5% de descuento'.",
            AccionDetectada = "ayuda"
        };
    }

    private string ResolveApiKey()
        => NormalizeApiKey(FirstNonEmpty(
            _settings.ApiKey,
            _configuration["OpenAI:ApiKey"],
            _configuration["OpenAI__ApiKey"],
            _configuration["OpenAI:Token"],
            _configuration["OpenAI__Token"],
            _configuration["OpenAi:ApiKey"],
            _configuration["OpenAi__ApiKey"],
            _configuration["OPENAI_API_KEY"],
            _configuration["OPENAI_TOKEN"],
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_TOKEN"),
            Environment.GetEnvironmentVariable("OpenAI__ApiKey"),
            Environment.GetEnvironmentVariable("OpenAI:ApiKey"),
            Environment.GetEnvironmentVariable("OpenAi__ApiKey"),
            Environment.GetEnvironmentVariable("OpenAi:ApiKey")));

    private string ResolveModel()
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            Environment.GetEnvironmentVariable("OpenAI__Model"),
            Environment.GetEnvironmentVariable("OpenAI:Model"),
            _settings.Model);

    private string ResolveBaseUrl()
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("OPENAI_BASE_URL"),
            Environment.GetEnvironmentVariable("OpenAI__BaseUrl"),
            Environment.GetEnvironmentVariable("OpenAI:BaseUrl"),
            _settings.BaseUrl);

    private string ResolveApiKeySource()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            return "options:OpenAI";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAI:ApiKey"]))
            return "config:OpenAI:ApiKey";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAI__ApiKey"]))
            return "config:OpenAI__ApiKey";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAI:Token"]))
            return "config:OpenAI:Token";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAI__Token"]))
            return "config:OpenAI__Token";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAi:ApiKey"]))
            return "config:OpenAi:ApiKey";
        if (!string.IsNullOrWhiteSpace(_configuration["OpenAi__ApiKey"]))
            return "config:OpenAi__ApiKey";
        if (!string.IsNullOrWhiteSpace(_configuration["OPENAI_API_KEY"]))
            return "config:OPENAI_API_KEY";
        if (!string.IsNullOrWhiteSpace(_configuration["OPENAI_TOKEN"]))
            return "config:OPENAI_TOKEN";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            return "env:OPENAI_API_KEY";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_TOKEN")))
            return "env:OPENAI_TOKEN";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenAI__ApiKey")))
            return "env:OpenAI__ApiKey";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenAI:ApiKey")))
            return "env:OpenAI:ApiKey";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenAi__ApiKey")))
            return "env:OpenAi__ApiKey";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenAi:ApiKey")))
            return "env:OpenAi:ApiKey";

        return "none";
    }

    private async Task<OpenAIAsistenteResult?> TryProcesarRapidoCoreAsync(FacturaConversationState state, string mensaje, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcesarConFallbackAsync(state, mensaje, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fast path local del asistente no pudo resolver el mensaje. Se continuara con OpenAI.");
            return null;
        }
    }

    private static bool ShouldUseLocalFastPath(FacturaConversationState state, string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (ContainsAny(normalized, "cancela", "cancelar", "me equivoque", "me equivoqué"))
            return true;

        if (ContainsAny(normalized, "resumen", "como va", "cómo va", "muestra la factura"))
            return true;

        if (state.Estado == FacturaConversationStates.EsperandoConfirmacion &&
            ContainsAny(normalized, "si", "sí", "confirmo", "dale", "correcto", "emit"))
            return true;

        if (IsNotaCreditoIntent(normalized))
            return true;

        if (ContainsAny(normalized, "forma de pago", "efectivo", "contado", "credito", "crédito"))
            return true;

        if (ContainsAny(normalized, "cuentas por cobrar", "cartera", "facturas pendientes", "saldo a favor", "abono", "abonar", "registrar pago", "registrar abono", "clientes que deben", "clientes con cartera", "clientes con saldo pendiente", "quienes deben", "quien me debe"))
            return true;

        if (state.Draft.Cliente is not null && state.Draft.Items.Count > 0 &&
            ContainsAny(normalized, "emitir", "emite", "finaliza", "finalizar"))
            return true;

        if (state.Draft.Items.Count == 0)
            return false;

        return ContainsAny(
            normalized,
            "quita",
            "elimina",
            "borra",
            "remueve",
            "saca",
            "cantidad",
            "precio",
            "iva",
            "descuento",
            "sube",
            "subir",
            "incrementa",
            "agrega",
            "aumenta",
            "baja",
            "bajar",
            "reduce",
            "reducir",
            "resta",
            "disminuye",
            "pon",
            "deja",
            "actualiza",
            "modifica",
            "cambia");
    }

    private static string FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private static string NormalizeApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        const string bearerPrefix = "Bearer ";
        return apiKey.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? apiKey[bearerPrefix.Length..].Trim()
            : apiKey.Trim();
    }

    private static bool ContainsAny(string text, params string[] tokens)
        => tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool IsNotaCreditoIntent(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var mentionsNotaCredito =
            (normalized.Contains("nota", StringComparison.OrdinalIgnoreCase) &&
             (normalized.Contains("credito", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("crédito", StringComparison.OrdinalIgnoreCase))) ||
            normalized.Contains("nota de credito", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("nota de crédito", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("notas de credito", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("notas de crédito", StringComparison.OrdinalIgnoreCase);

        return mentionsNotaCredito;
    }

    private static string? ResolveNotaCreditoRoute(string normalized)
    {
        if (!IsNotaCreditoIntent(normalized))
            return null;

        if (ContainsAny(normalized, "historial", "generadas", "emitidas", "listado", "lista", "ver notas"))
            return "/facturacion/notas-credito-generadas";

        return "/facturacion/nota-credito";
    }

    private async Task<OpenAIAsistenteResult?> TryHandleNotaCreditoCommandAsync(
        FacturaConversationState state,
        string mensaje,
        string normalized,
        CancellationToken cancellationToken)
    {
        if (!IsNotaCreditoIntent(normalized))
            return null;

        if (!ContainsAny(normalized, "emit", "emite", "genera", "crear", "crea", "haz"))
            return null;

        var referenciaFactura = ExtractInvoiceReferenceForNotaCredito(mensaje);
        if (string.IsNullOrWhiteSpace(referenciaFactura))
        {
            return new OpenAIAsistenteResult
            {
                Respuesta = "Puedo emitir la nota de credito automatica, pero necesito el numero de la factura autorizada de origen.",
                AccionDetectada = "emitir_nota_credito"
            };
        }

        var motivo = ExtractNotaCreditoReason(mensaje);
        var result = await _toolDispatcher.DispatchAsync(
            ToolDefinitions.EmitirNotaCreditoDesdeFactura,
            JsonSerializer.Serialize(new { referenciaFactura, motivo }),
            state,
            cancellationToken);

        return new OpenAIAsistenteResult
        {
            Respuesta = result.Message,
            AccionDetectada = "emitir_nota_credito"
        };
    }

    private static string? ExtractInvoiceReferenceForNotaCredito(string mensaje)
    {
        var directMatch = Regex.Match(mensaje, @"\bfactura\s*(?:n(?:u|ú)mero\s*)?(?<num>\d{3,9})\b", RegexOptions.IgnoreCase);
        if (directMatch.Success)
            return directMatch.Groups["num"].Value;

        var genericMatch = Regex.Match(mensaje, @"\b(?<num>\d{3,9})\b");
        return genericMatch.Success ? genericMatch.Groups["num"].Value : null;
    }

    private static string? ExtractNotaCreditoReason(string mensaje)
    {
        var motivoMatch = Regex.Match(mensaje, @"(?:motivo|por)\s+(?<motivo>.+)$", RegexOptions.IgnoreCase);
        return motivoMatch.Success ? motivoMatch.Groups["motivo"].Value.Trim() : null;
    }

    private static string ClipForModel(string? content, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var compact = content.Trim();
        return compact.Length <= maxLength
            ? compact
            : $"{compact[..maxLength]}...";
    }

    private async Task<OpenAIAsistenteResult?> TryHandleDraftVoiceCommandAsync(
        FacturaConversationState state,
        string mensaje,
        string normalized,
        CancellationToken cancellationToken)
    {
        if (state.Draft.Items.Count == 0)
            return null;

        var referencia = ResolveItemReference(state, normalized);

        var ajusteCantidadMatch = Regex.Match(
            normalized,
            @"\b(?<verbo>sube(?:le)?|subir|incrementa(?:le)?|agrega(?:le)?|aumenta(?:le)?|baja(?:le)?|bajar|reduce|reducir|resta(?:le)?|disminuye(?:le)?|quita(?:le)?)\s+(?<delta>\d+(?:[.,]\d+)?|un|una|uno)\s+(?:unidad|unidades)\b",
            RegexOptions.IgnoreCase);
        if (ajusteCantidadMatch.Success)
        {
            referencia ??= ResolveSingleItemReference(state);
            if (referencia is null)
            {
                return BuildClarificationResult("Hay varios productos en la factura. Dime a cuál cambiarle la cantidad, por ejemplo: 'súbele una unidad al segundo producto'.");
            }

            var item = state.Draft.Items.FirstOrDefault(x => x.Id == referencia);
            if (item is null)
                return null;

            var delta = ParseAmountToken(ajusteCantidadMatch.Groups["delta"].Value) ?? 0m;
            var verbo = ajusteCantidadMatch.Groups["verbo"].Value;
            var increase = ContainsAny(verbo, "sub", "increment", "agrega", "aumenta");
            var nuevaCantidad = increase ? item.Cantidad + delta : item.Cantidad - delta;

            var result = nuevaCantidad <= 0m
                ? await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.QuitarProductoDeFactura,
                    JsonSerializer.Serialize(new { referenciaItem = referencia }),
                    state,
                    cancellationToken)
                : await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.ModificarCantidadProducto,
                    JsonSerializer.Serialize(new { referenciaItem = referencia, cantidad = nuevaCantidad }),
                    state,
                    cancellationToken);

            return BuildResult(result, nuevaCantidad <= 0m ? "quitar_item" : "modificar_cantidad");
        }

        if (ContainsAny(normalized, "quita", "elimina", "borra", "remueve", "saca"))
        {
            if (referencia is null)
            {
                return BuildClarificationResult("Indícame qué producto quieres quitar. Por ejemplo: 'quita el segundo producto'.");
            }

            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.QuitarProductoDeFactura,
                JsonSerializer.Serialize(new { referenciaItem = referencia }),
                state,
                cancellationToken);

            return BuildResult(result, "quitar_item");
        }

        var cantidadMatch = Regex.Match(
            normalized,
            @"\b(?:pon|ponga|poner|cambia|cambiar|deja|dejar|actualiza|actualizar)\s+(?<cantidad>\d+(?:[.,]\d+)?)\s+(?:unidad|unidades|cantidad)\b",
            RegexOptions.IgnoreCase);
        if (!cantidadMatch.Success)
        {
            cantidadMatch = Regex.Match(
                normalized,
                @"\b(?:sube|subir|baja|bajar|cambia|cambiar|pon|poner|deja|dejar|actualiza|actualizar)\b.*?\bcantidad\b.*?(?:a|en)?\s*(?<cantidad>\d+(?:[.,]\d+)?)\b",
                RegexOptions.IgnoreCase);
        }
        if (cantidadMatch.Success)
        {
            referencia ??= ResolveSingleItemReference(state);
            if (referencia is null)
            {
                return BuildClarificationResult("Hay varios productos en la factura. Dime cuál cambiar, por ejemplo: 'pon 3 unidades al segundo producto'.");
            }

            var cantidad = ParseDecimal(cantidadMatch.Groups["cantidad"].Value) ?? 0m;
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.ModificarCantidadProducto,
                JsonSerializer.Serialize(new { referenciaItem = referencia, cantidad }),
                state,
                cancellationToken);

            return BuildResult(result, "modificar_cantidad");
        }

        var ivaMatch = Regex.Match(
            normalized,
            @"\b(?:cambia|cambiar|pon|poner|deja|dejar|actualiza|actualizar|modifica|modificar|sube(?:le)?|baja(?:le)?)\s+(?:el\s+)?iva\b.*?(?:a|al|en)?\s*(?<iva>\d+(?:[.,]\d+)?)\b",
            RegexOptions.IgnoreCase);
        if (ivaMatch.Success)
        {
            referencia ??= ResolveSingleItemReference(state);
            if (referencia is null)
            {
                return BuildClarificationResult("Hay varios productos en la factura. Dime a cuál cambiarle el IVA, por ejemplo: 'cambia el IVA a 15 al segundo producto'.");
            }

            var tarifa = ParseDecimal(ivaMatch.Groups["iva"].Value) ?? 0m;
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.ModificarIvaItem,
                JsonSerializer.Serialize(new { referenciaItem = referencia, tarifaPorcentaje = tarifa }),
                state,
                cancellationToken);

            return BuildResult(result, "modificar_iva");
        }

        var precioMatch = Regex.Match(
            normalized,
            @"\b(?:pon|poner|cambia|cambiar|deja|dejar|actualiza|actualizar|modifica|modificar)\s+(?:el\s+)?precio\b.*?(?:a|al|en)?\s*(?<precio>\d+(?:[.,]\d+)?)\b",
            RegexOptions.IgnoreCase);
        if (!precioMatch.Success)
        {
            precioMatch = Regex.Match(
                normalized,
                @"\bprecio\s+(?<precio>\d+(?:[.,]\d+)?)\b",
                RegexOptions.IgnoreCase);
        }
        if (precioMatch.Success)
        {
            referencia ??= ResolveSingleItemReference(state);
            if (referencia is null)
            {
                return BuildClarificationResult("Hay varios productos en la factura. Dime a cuál cambiarle el precio, por ejemplo: 'pon el precio en 12.50 al tercer producto'.");
            }

            var precio = ParseDecimal(precioMatch.Groups["precio"].Value) ?? 0m;
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.ModificarPrecioItem,
                JsonSerializer.Serialize(new { referenciaItem = referencia, precioUnitario = precio }),
                state,
                cancellationToken);

            return BuildResult(result, "modificar_precio");
        }

        var descuentoMatch = Regex.Match(
            normalized,
            @"\b(?:aplica|aplicar|pon|poner|deja|dejar|cambia|cambiar)\s+(?<descuento>\d+(?:[.,]\d+)?)\s*(?:%|por ciento|porciento)\s+de\s+descuento\b",
            RegexOptions.IgnoreCase);
        if (descuentoMatch.Success)
        {
            var descuento = ParseDecimal(descuentoMatch.Groups["descuento"].Value) ?? 0m;

            if (ContainsAny(normalized, "global", "toda la factura", "factura completa", "a toda la factura"))
            {
                var globalResult = await _toolDispatcher.DispatchAsync(
                    ToolDefinitions.AplicarDescuentoGlobal,
                    JsonSerializer.Serialize(new { porcentaje = descuento }),
                    state,
                    cancellationToken);

                return BuildResult(globalResult, "descuento_global");
            }

            referencia ??= ResolveSingleItemReference(state);
            if (referencia is null)
            {
                return BuildClarificationResult("Hay varios productos en la factura. Dime a cuál aplicar el descuento, por ejemplo: 'aplica 10 por ciento de descuento al primer producto'.");
            }

            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.AplicarDescuentoLinea,
                JsonSerializer.Serialize(new { referenciaItem = referencia, porcentaje = descuento }),
                state,
                cancellationToken);

            return BuildResult(result, "descuento_linea");
        }

        return null;
    }

    private async Task<OpenAIAsistenteResult?> TryHandlePaymentMethodCommandAsync(
        FacturaConversationState state,
        string normalized,
        CancellationToken cancellationToken)
    {
        if (ContainsAny(normalized, "crea", "crear", "haz", "genera", "factura", "emite"))
            return null;

        if (!ContainsAny(normalized, "forma de pago", "efectivo", "contado", "credito", "crédito"))
            return null;

        var formaPago = DetectPaymentMethod(normalized);

        if (string.IsNullOrWhiteSpace(formaPago))
            return null;

        var diasCredito = ParseCreditDays(normalized);
        var result = await _toolDispatcher.DispatchAsync(
            ToolDefinitions.ModificarFormaPago,
            JsonSerializer.Serialize(new { formaPago, diasCredito }),
            state,
            cancellationToken);

        return BuildResult(result, "cambiar_forma_pago");
    }

    private async Task<OpenAIAsistenteResult?> TryHandleCollectionCommandAsync(
        FacturaConversationState state,
        string mensaje,
        string normalized,
        CancellationToken cancellationToken)
    {
        if (ContainsAny(normalized, "saldo a favor"))
        {
            var filtroCliente = TryExtractClientHintFlexible(mensaje);
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.ConsultarSaldoAFavor,
                JsonSerializer.Serialize(new { filtroCliente }),
                state,
                cancellationToken);

            return BuildResult(result, "consultar_saldo_a_favor");
        }

        if (ContainsAny(normalized, "clientes con cuentas por cobrar", "clientes con cartera", "clientes con saldo pendiente", "clientes que deben", "quienes deben", "quien me debe", "quienes me deben"))
        {
            var filtroCliente = TryExtractClientHintFlexible(mensaje);
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.BuscarClientesConCuentasPorCobrar,
                JsonSerializer.Serialize(new { filtroCliente }),
                state,
                cancellationToken);

            return BuildResult(result, "buscar_clientes_con_cartera");
        }

        if (ContainsAny(normalized, "cuentas por cobrar", "cartera", "facturas pendientes"))
        {
            var filtroCliente = TryExtractClientHintFlexible(mensaje);
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.ConsultarCuentasPorCobrar,
                JsonSerializer.Serialize(new { filtroCliente }),
                state,
                cancellationToken);

            return BuildResult(result, "consultar_cuentas_por_cobrar");
        }

        if (ContainsAny(normalized, "abono", "abonar", "registrar pago", "registrar abono"))
        {
            var monto = TryExtractAmount(mensaje);
            if (!monto.HasValue || monto <= 0m)
            {
                return new OpenAIAsistenteResult
                {
                    Respuesta = "Indícame el monto del abono para registrarlo. Ejemplo: 'registra un abono de 150 al cliente Juan Pérez'.",
                    AccionDetectada = "registrar_abono"
                };
            }

            var filtroCliente = TryExtractClientHintFlexible(mensaje);
            var result = await _toolDispatcher.DispatchAsync(
                ToolDefinitions.RegistrarAbonoGeneral,
                JsonSerializer.Serialize(new
                {
                    monto,
                    filtroCliente,
                    observacion = "Abono registrado desde el asistente"
                }),
                state,
                cancellationToken);

            return BuildResult(result, "registrar_abono");
        }

        return null;
    }

    private static OpenAIAsistenteResult BuildResult(ToolResultDto result, string action)
        => new()
        {
            Respuesta = result.Message,
            AccionDetectada = action
        };

    private static OpenAIAsistenteResult BuildClarificationResult(string message)
        => new()
        {
            Respuesta = message,
            AccionDetectada = "aclaracion_item"
        };

    private static string? ResolveItemReference(FacturaConversationState state, string normalized)
    {
        if (state.Draft.Items.Count == 0)
            return null;

        var ordinalMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["primer"] = 0,
            ["primero"] = 0,
            ["primera"] = 0,
            ["segundo"] = 1,
            ["segunda"] = 1,
            ["tercero"] = 2,
            ["tercera"] = 2,
            ["cuarto"] = 3,
            ["cuarta"] = 3,
            ["quinto"] = 4,
            ["quinta"] = 4
        };

        foreach (var pair in ordinalMap)
        {
            if (normalized.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) && state.Draft.Items.Count > pair.Value)
                return state.Draft.Items[pair.Value].Id;
        }

        if (ContainsAny(normalized, "último", "ultimo", "final") && state.Draft.Items.Count > 0)
            return state.Draft.Items[^1].Id;

        var explicitNumberMatch = Regex.Match(normalized, @"\bproducto\s+(?<indice>\d+)\b", RegexOptions.IgnoreCase);
        if (explicitNumberMatch.Success && int.TryParse(explicitNumberMatch.Groups["indice"].Value, out var indiceNumerico))
        {
            var index = indiceNumerico - 1;
            if (index >= 0 && index < state.Draft.Items.Count)
                return state.Draft.Items[index].Id;
        }

        var itemByName = state.Draft.Items.FirstOrDefault(x =>
            normalized.Contains((x.Descripcion ?? string.Empty).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(x.CodigoPrincipal) && normalized.Contains(x.CodigoPrincipal.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)));

        return itemByName?.Id;
    }

    private static string? ResolveSingleItemReference(FacturaConversationState state)
        => state.Draft.Items.Count == 1 ? state.Draft.Items[0].Id : null;

    private static decimal? ParseDecimal(string input)
        => decimal.TryParse(
            input.Replace(",", "."),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    private static decimal? ParseAmountToken(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim().ToLowerInvariant();
        if (normalized is "un" or "una" or "uno")
            return 1m;
        if (normalized == "dos")
            return 2m;
        if (normalized == "tres")
            return 3m;
        if (normalized == "cuatro")
            return 4m;
        if (normalized == "cinco")
            return 5m;
        if (normalized == "seis")
            return 6m;
        if (normalized == "siete")
            return 7m;
        if (normalized == "ocho")
            return 8m;
        if (normalized == "nueve")
            return 9m;
        if (normalized == "diez")
            return 10m;

        return ParseDecimal(input);
    }

    private static string? DetectPaymentMethod(string normalized)
    {
        if (normalized.Contains("credit", StringComparison.OrdinalIgnoreCase) || normalized.Contains("crédito", StringComparison.OrdinalIgnoreCase))
            return "Crédito";

        if (normalized.Contains("efectivo", StringComparison.OrdinalIgnoreCase) || normalized.Contains("contado", StringComparison.OrdinalIgnoreCase))
            return "Efectivo";

        return null;
    }

    private static int? ParseCreditDays(string input)
    {
        var match = Regex.Match(input, @"(?<dias>\d{1,3})\s*d[ií]as?", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return int.TryParse(match.Groups["dias"].Value, out var dias) && dias > 0
            ? dias
            : null;
    }

    private static decimal? TryExtractAmount(string input)
    {
        var match = Regex.Match(input, @"(?:\$|\bde\b|\bpor\b|\babono\b)\s*(?<monto>\d+(?:[.,]\d{1,2})?)", RegexOptions.IgnoreCase);
        return match.Success
            ? ParseDecimal(match.Groups["monto"].Value)
            : null;
    }

    private static string? TryExtractClientHint(string input)
    {
        var match = Regex.Match(input, @"(?:cliente|de)\s+(?<cliente>[A-Za-zÁÉÍÓÚÑ0-9\s\.]+)$", RegexOptions.IgnoreCase);
        return match.Success
            ? match.Groups["cliente"].Value.Trim()
            : null;
    }

    private static string? TryExtractClientHintFlexible(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var patterns = new[]
        {
            @"(?:cliente|clientes?|de|del|para|a)\s+(?<cliente>[\p{L}0-9][\p{L}0-9\s\.\-']+?)(?=\s+(?:con|por|que|pendiente|pendientes|saldo|cartera|cuentas|facturas|abono|abonar)\b|$)",
            @"(?:cliente|clientes?|de|del|para|a)\s+(?<cliente>[\p{L}0-9][\p{L}0-9\s\.\-']+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            var cliente = match.Groups["cliente"].Value.Trim(' ', '.', ',', ';', ':');
            if (!string.IsNullOrWhiteSpace(cliente))
                return cliente;
        }

        return TryExtractClientHint(input);
    }

    private static string? ExtractContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var contentProperty))
            return null;

        if (contentProperty.ValueKind == JsonValueKind.String)
            return contentProperty.GetString();

        if (contentProperty.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        foreach (var item in contentProperty.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text))
                parts.Add(text.GetString() ?? string.Empty);
        }

        return string.Join("", parts);
    }

    private static List<ToolCallEnvelope> ParseToolCalls(JsonElement message)
    {
        var result = new List<ToolCallEnvelope>();
        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in toolCalls.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
            var function = item.GetProperty("function");
            result.Add(new ToolCallEnvelope
            {
                Id = id,
                Name = function.GetProperty("name").GetString() ?? string.Empty,
                Arguments = function.TryGetProperty("arguments", out var args) ? args.GetString() : "{}"
            });
        }

        return result;
    }

    private sealed class ToolCallEnvelope
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Arguments { get; set; }
    }
}
