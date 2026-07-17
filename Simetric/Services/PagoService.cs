namespace Simetric.Services
{
    using Microsoft.AspNetCore.WebUtilities;
    using Simetric.DTOs;
    using Simetric.Models;
    using System.Diagnostics;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;

    public class PagoService
    {
        private const string DefaultBaseUrl = "https://api.abitmedia.cloud/pagomedios/v2";
        private const string DevelopmentTokenFallback = "denljywrk5yafpzaqcfrpgzvj6skkxiev1ezh1hodiozgyjxadfymjgxtcwg1wpu2fbgr";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null,
            WriteIndented = true
        };

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PagoService> _logger;

        public PagoService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<PagoService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public string BaseUrl => (_configuration["Pagomedios:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');

        public string? ConfiguredToken => ResolveBearerToken();

        public async Task<string?> GenerarLinkPago(PagomediosRequest request)
        {
            var result = await SendJsonAsync(
                HttpMethod.Post,
                "/payment-links",
                request);

            return result.IsSuccess ? ExtractPaymentUrl(result.ResponseBody) : null;
        }

        public async Task<string?> GenerarSolicitudPago(UsuSolicitudFirma sol, string? notifyUrl = null)
        {
            decimal subtotal = Math.Round(sol.SolMontoPago ?? 0, 2);
            decimal iva = Math.Round(subtotal * 0.15m, 2);
            decimal total = Math.Round(subtotal + iva, 2);
            var esignBearerToken = _configuration["Pagomedios:ESignBearerToken"];

            var request = new
            {
                integration = true,
                third = new
                {
                    document = sol.SolIdentificacion,
                    document_type = ObtenerTipoDocumentoPagomedios(sol),
                    name = $"{sol.SolNombres} {sol.SolPrimerApellido}".Trim(),
                    email = sol.SolCorreo1,
                    phones = string.IsNullOrWhiteSpace(sol.SolTelefono1) ? "0999999999" : sol.SolTelefono1,
                    address = string.IsNullOrWhiteSpace(sol.SolDireccion) ? "Quito" : sol.SolDireccion,
                    type = string.Equals(sol.SolTipoPersona, "JURIDICA", StringComparison.OrdinalIgnoreCase)
                        ? "Company"
                        : "Individual"
                },
                generate_invoice = 0,
                description = "Pago de Firma Electronica E-FACT",
                amount = total,
                amount_with_tax = subtotal,
                amount_without_tax = 0m,
                tax_value = iva,
                settings = Array.Empty<string>(),
                has_cards = 1,
                has_de_una = 1,
                has_paypal = 0,
                has_safetypay = false,
                notify_url = string.IsNullOrWhiteSpace(notifyUrl) ? null : notifyUrl,
                custom_value = sol.SolId.ToString()
            };

            var result = await SendJsonAsync(
                HttpMethod.Post,
                "/payment-requests",
                request,
                bearerTokenOverride: esignBearerToken,
                preserveHasCards: true);

            if (result.IsSuccess)
            {
                return ExtractPaymentUrl(result.ResponseBody);
            }

            _logger.LogError(
                "Pagomedios rechazo la solicitud de pago. Status: {Status}. Error: {Error}. Body: {Body}",
                result.StatusCode,
                result.ErrorMessage,
                result.ResponseBody);

            return null;
        }

        public async Task<PagomediosApiResult> SendJsonAsync(
            HttpMethod method,
            string path,
            object body,
            IReadOnlyDictionary<string, string?>? query = null,
            string? bearerTokenOverride = null,
            bool preserveHasCards = false)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            json = NormalizeHasCardsForPaymentRequest(path, json, preserveHasCards);

            _logger.LogInformation(
                "Pagomedios request {Method} {Path} payload JSON: {Json}",
                method.Method,
                path,
                json);

            return await SendRawAsync(method.Method, path, json, query, bearerTokenOverride);
        }

        private string NormalizeHasCardsForPaymentRequest(string path, string json, bool preserveHasCards = false)
        {
            if (string.IsNullOrWhiteSpace(json) ||
                !(path.Contains("/payment-requests", StringComparison.OrdinalIgnoreCase) ||
                  path.Contains("/payment-links", StringComparison.OrdinalIgnoreCase)))
            {
                return json;
            }

            try
            {
                var node = JsonNode.Parse(json) as JsonObject;
                if (node is null || !node.TryGetPropertyValue("has_cards", out var hasCardsNode))
                {
                    return json;
                }

                var hasCards = hasCardsNode?.GetValue<int?>() ?? 0;
                if (hasCards == 0)
                {
                    return json;
                }

                if (preserveHasCards && IsESignCardCheckout(node))
                {
                    return json;
                }

                node["has_cards"] = 0;

                var normalizedJson = node.ToJsonString(JsonOptions);

                _logger.LogWarning(
                    "Se normalizo has_cards de {OriginalValue} a 0 para la solicitud {Path}. JSON normalizado: {Json}",
                    hasCards,
                    path,
                    normalizedJson);

                return normalizedJson;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo normalizar has_cards para la solicitud {Path}. Se enviara el JSON original.",
                    path);

                return json;
            }
        }

        private static bool IsESignCardCheckout(JsonObject node)
        {
            var description = node["description"]?.GetValue<string>() ?? string.Empty;
            var customValue = node["custom_value"]?.GetValue<string>() ?? string.Empty;

            return description.Contains("E-SIGN", StringComparison.OrdinalIgnoreCase) ||
                   description.Contains("Firma Electronica", StringComparison.OrdinalIgnoreCase) ||
                   customValue.Contains("esign", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<PagomediosApiResult> SendRawAsync(
            string method,
            string path,
            string? jsonBody,
            IReadOnlyDictionary<string, string?>? query = null,
            string? bearerTokenOverride = null)
        {
            var result = new PagomediosApiResult
            {
                Method = method.ToUpperInvariant(),
                Path = path,
                SentAt = DateTimeOffset.Now
            };

            var token = ResolveBearerToken(bearerTokenOverride);
            if (string.IsNullOrWhiteSpace(token))
            {
                result.ErrorMessage = "Configura Pagomedios:BearerToken, Pagomedios:Token, la variable PAGOMEDIOS_BEARER_TOKEN o pega un token temporal.";
                return result;
            }

            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }

            try
            {
                var uri = BuildUri(path, query);
                result.RequestUrl = uri.ToString();

                using var request = new HttpRequestMessage(new HttpMethod(result.Method), uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (RequiresBody(result.Method) && !string.IsNullOrWhiteSpace(jsonBody))
                {
                    ValidateJson(jsonBody);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                var stopwatch = Stopwatch.StartNew();
                using var response = await _httpClient.SendAsync(request);
                stopwatch.Stop();

                var responseBody = await response.Content.ReadAsStringAsync();

                result.StatusCode = (int)response.StatusCode;
                result.ReasonPhrase = response.ReasonPhrase;
                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                result.IsSuccess = response.IsSuccessStatusCode;
                result.ResponseBody = PrettyJson(responseBody);
                result.PaymentUrl = ExtractPaymentUrl(result.ResponseBody);

                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                }
            }
            catch (JsonException ex)
            {
                result.ErrorMessage = $"El cuerpo JSON no es valido: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error consumiendo Pagomedios {Method} {Path}", method, path);
            }

            return result;
        }

        private Uri BuildUri(string path, IReadOnlyDictionary<string, string?>? query)
        {
            var url = $"{BaseUrl}{path}";
            var cleanQuery = query?
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key, item => item.Value);

            if (cleanQuery is { Count: > 0 })
            {
                url = QueryHelpers.AddQueryString(url, cleanQuery);
            }

            return new Uri(url, UriKind.Absolute);
        }

        private string? ResolveBearerToken(string? bearerTokenOverride = null)
        {
            var token = FirstNonEmpty(
                bearerTokenOverride,
                _configuration["Pagomedios:BearerToken"],
                _configuration["Pagomedios:Token"],
                Environment.GetEnvironmentVariable("PAGOMEDIOS_BEARER_TOKEN"),
                DevelopmentTokenFallback);

            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            const string bearerPrefix = "Bearer ";
            return token.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? token[bearerPrefix.Length..].Trim()
                : token.Trim();
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static bool RequiresBody(string method) =>
            string.Equals(method, HttpMethod.Post.Method, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(method, HttpMethod.Put.Method, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(method, HttpMethod.Patch.Method, StringComparison.OrdinalIgnoreCase);

        private static void ValidateJson(string jsonBody)
        {
            using var _ = JsonDocument.Parse(jsonBody);
        }

        private static string PrettyJson(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                return JsonSerializer.Serialize(document.RootElement, JsonOptions);
            }
            catch (JsonException)
            {
                return responseBody;
            }
        }

        private static string? ExtractPaymentUrl(string? responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("url", out var dataUrl))
                {
                    return dataUrl.GetString();
                }

                if (root.TryGetProperty("data", out data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("url", out var itemUrl) &&
                            itemUrl.ValueKind == JsonValueKind.String)
                        {
                            return itemUrl.GetString();
                        }
                    }
                }

                if (root.TryGetProperty("url", out var url))
                {
                    return url.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private static string ObtenerTipoDocumentoPagomedios(UsuSolicitudFirma sol)
        {
            if (string.Equals(sol.SolTipoIdentificacion, "PASAPORTE", StringComparison.OrdinalIgnoreCase))
            {
                return "06";
            }

            return sol.SolIdentificacion?.Length == 13 ? "04" : "05";
        }

        public async Task<PagomediosApiResult> ObtenerTarjetasTokenizadas(string document)
        {
            var query = new Dictionary<string, string?>
            {
                { "integration", "true" },
                { "document", document }
            };

            return await SendRawAsync("GET", "/cards", null, query);
        }

        public async Task<PagomediosApiResult> RegistrarTarjetaSuscripcion(object request)
        {
            return await SendJsonAsync(HttpMethod.Post, "/cards/register", request);
        }

        public async Task<PagomediosApiResult> CobrarTarjetaTokenizada(object request)
        {
            return await SendJsonAsync(HttpMethod.Post, "/cards/charge", request);
        }

        public async Task<PagomediosApiResult> EliminarTarjetaTokenizada(string token)
        {
            return await SendRawAsync("DELETE", $"/cards/{token}", null);
        }
    }

    public class PagomediosApiResult
    {
        public bool IsSuccess { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string RequestUrl { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public string? ReasonPhrase { get; set; }
        public string? ResponseBody { get; set; }
        public string? PaymentUrl { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset SentAt { get; set; }
        public long ElapsedMilliseconds { get; set; }
    }
}
