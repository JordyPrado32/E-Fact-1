using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace Simetric.Services;

public sealed class WhatsAppCloudApiOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v23.0";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RecipientPhoneNumber { get; set; } = string.Empty;
    public string FallbackPhoneNumber { get; set; } = "593995876753";
    public string TechnicalAdvisorName { get; set; } = "Arleth";
    public string CommercialAdvisorName { get; set; } = "Brigitte";
    public string TechnicalAdvisorPhoneNumber { get; set; } = "593995876753";
    public string CommercialAdvisorPhoneNumber { get; set; } = "593995876753";
}

public sealed class WhatsAppSupportRequest
{
    public string Categoria { get; set; } = "Soporte tecnico";
    public string Asesor { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Usuario { get; set; } = string.Empty;
    public string Ruc { get; set; } = string.Empty;
    public string Consulta { get; set; } = string.Empty;
    public string AdvisorPhoneNumber { get; set; } = string.Empty;
}

public sealed class WhatsAppSupportSendResult
{
    public bool Success { get; set; }
    public bool SentViaApi { get; set; }
    public bool UsedFallback { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
}

public sealed class WhatsAppSupportService
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppCloudApiOptions _options;
    private readonly ILogger<WhatsAppSupportService> _logger;

    public WhatsAppSupportService(
        HttpClient httpClient,
        IOptions<WhatsAppCloudApiOptions> options,
        ILogger<WhatsAppSupportService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ResolveAdvisorName(string categoria)
        => categoria.Contains("comercial", StringComparison.OrdinalIgnoreCase)
            ? _options.CommercialAdvisorName
            : _options.TechnicalAdvisorName;

    public string BuildMessage(WhatsAppSupportRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Hola.");
        sb.AppendLine();
        sb.AppendLine($"Necesito ayuda en: {request.Categoria.Trim()}.");
        sb.AppendLine($"Empresa: {request.Empresa.Trim()}");
        sb.AppendLine($"Usuario: {request.Usuario.Trim()}");
        sb.Append("Mi consulta es: ");
        sb.Append(request.Consulta.Trim());
        return sb.ToString();
    }

    public string BuildFallbackUrl(WhatsAppSupportRequest request)
    {
        var number = ResolveAdvisorPhoneNumber(request.Categoria, request.Asesor, request.AdvisorPhoneNumber);
        var text = Uri.EscapeDataString(BuildMessage(request));
        return $"https://wa.me/{number}?text={text}";
    }

    public string BuildExternalChatUrl(WhatsAppSupportRequest request)
        => BuildFallbackUrl(request);

    public string ResolveAdvisorPhoneNumber(string categoria, string? advisor = null, string? explicitPhoneNumber = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPhoneNumber))
        {
            var normalizedExplicitNumber = NormalizePhone(explicitPhoneNumber);
            if (!string.IsNullOrWhiteSpace(normalizedExplicitNumber))
            {
                return normalizedExplicitNumber;
            }
        }

        var advisorName = string.IsNullOrWhiteSpace(advisor)
            ? ResolveAdvisorName(categoria)
            : advisor.Trim();

        if (advisorName.Equals(_options.CommercialAdvisorName, StringComparison.OrdinalIgnoreCase))
        {
            var commercialNumber = NormalizePhone(_options.CommercialAdvisorPhoneNumber);
            if (!string.IsNullOrWhiteSpace(commercialNumber))
            {
                return commercialNumber;
            }
        }

        var technicalNumber = NormalizePhone(_options.TechnicalAdvisorPhoneNumber);
        if (!string.IsNullOrWhiteSpace(technicalNumber))
        {
            return technicalNumber;
        }

        return NormalizePhone(_options.FallbackPhoneNumber);
    }

    public async Task<WhatsAppSupportSendResult> SendAsync(WhatsAppSupportRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Consulta))
        {
            return new WhatsAppSupportSendResult
            {
                Success = false,
                Message = "Escribe la consulta antes de enviarla."
            };
        }

        if (!CanUseCloudApi())
        {
            return new WhatsAppSupportSendResult
            {
                Success = true,
                UsedFallback = true,
                Message = "WhatsApp Cloud API no esta configurado todavia. Se abrira el chat externo como respaldo.",
                RedirectUrl = BuildFallbackUrl(request)
            };
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiVersion.Trim('/')}/{_options.PhoneNumberId.Trim()}/messages");

            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken.Trim());
            httpRequest.Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = NormalizePhone(_options.RecipientPhoneNumber),
                type = "text",
                text = new
                {
                    preview_url = false,
                    body = BuildMessage(request)
                }
            });

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new WhatsAppSupportSendResult
                {
                    Success = true,
                    SentViaApi = true,
                    Message = "Mensaje enviado correctamente por WhatsApp Cloud API."
                };
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "WhatsApp Cloud API devolvio error {StatusCode}. Respuesta: {Body}",
                (int)response.StatusCode,
                errorBody);

            return new WhatsAppSupportSendResult
            {
                Success = true,
                UsedFallback = true,
                Message = "No se pudo enviar por WhatsApp Cloud API. Se abrira el chat externo como respaldo.",
                RedirectUrl = BuildFallbackUrl(request)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo el envio por WhatsApp Cloud API.");
            return new WhatsAppSupportSendResult
            {
                Success = true,
                UsedFallback = true,
                Message = "Ocurrio un problema al conectar con WhatsApp Cloud API. Se abrira el chat externo como respaldo.",
                RedirectUrl = BuildFallbackUrl(request)
            };
        }
    }

    private bool CanUseCloudApi()
        => _options.Enabled
           && !string.IsNullOrWhiteSpace(_options.AccessToken)
           && !string.IsNullOrWhiteSpace(_options.PhoneNumberId)
           && !string.IsNullOrWhiteSpace(_options.RecipientPhoneNumber);

    private static string NormalizePhone(string? phone)
        => new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
}
