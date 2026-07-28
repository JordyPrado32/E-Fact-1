using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace Simetric.Services.ESign;

public sealed class FirmaStampApiService
{
    private const long MaxFileBytes = 15 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public FirmaStampApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<FirmaStampApiResult> EstamparAsync(
        IBrowserFile pdf,
        FirmaStampApiFile logo,
        string datos,
        int pagina,
        double xMm,
        double yMm,
        double anchoMm,
        CancellationToken cancellationToken = default)
    {
        var baseUri = GetBaseUri();
        var apiKey = GetApiKey();

        if (baseUri is null)
            return FirmaStampApiResult.Error("La URL de la API de estampado no esta configurada.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return FirmaStampApiResult.Error("La API key de estampado no esta configurada.");

        await using var pdfStream = pdf.OpenReadStream(MaxFileBytes, cancellationToken);
        await using var logoStream = new MemoryStream(logo.Content, writable: false);

        using var form = new MultipartFormDataContent();
        using var pdfContent = CreateFileContent(pdfStream, pdf.ContentType);
        using var logoContent = CreateFileContent(logoStream, logo.ContentType);

        form.Add(pdfContent, "pdf", pdf.Name);
        form.Add(logoContent, "logo", logo.FileName);
        form.Add(new StringContent(datos), "datos");
        form.Add(new StringContent(pagina.ToString(CultureInfo.InvariantCulture)), "pagina");
        form.Add(new StringContent(xMm.ToString(CultureInfo.InvariantCulture)), "xMm");
        form.Add(new StringContent(yMm.ToString(CultureInfo.InvariantCulture)), "yMm");
        form.Add(new StringContent(anchoMm.ToString(CultureInfo.InvariantCulture)), "anchoMm");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/documentos/estampar"));
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Content = form;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var raw = System.Text.Encoding.UTF8.GetString(bytes);
                var mensaje = ExtraerMensajeError(raw)
                    ?? $"La API de firma respondio con estado {(int)response.StatusCode}.";

                return FirmaStampApiResult.Error(mensaje, raw, (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
            var originalHash = TryGetHeader(response, "X-Documento-Original-SHA256");
            var outputHash = TryGetHeader(response, "X-Documento-Salida-SHA256");

            return FirmaStampApiResult.Ok(bytes, contentType, originalHash, outputHash, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FirmaStampApiResult.Error("La API de firma excedio el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return FirmaStampApiResult.Error("No fue posible conectar con la API de firma.");
        }
    }

    public async Task<QrValidationApiResult> ValidarQrAsync(
        string entrada,
        CancellationToken cancellationToken = default)
    {
        var baseUri = GetBaseUri();
        if (baseUri is null)
            return QrValidationApiResult.Error("La URL de la API de estampado no esta configurada.");

        if (!TryGetQrToken(entrada, out var token, out var validationError))
            return QrValidationApiResult.Error(validationError);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, $"api/verificaciones/qr?token={Uri.EscapeDataString(token)}"));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var mensaje = ExtraerMensajeError(raw)
                    ?? $"La API de firma respondio con estado {(int)response.StatusCode}.";

                return QrValidationApiResult.Error(mensaje, raw, (int)response.StatusCode);
            }

            var verification = JsonSerializer.Deserialize<QrVerificationApiResponse>(
                raw,
                JsonOptions);

            if (verification is null || !verification.Valido)
                return QrValidationApiResult.Error("La API no devolvio un resultado de verificacion valido.", raw, (int)response.StatusCode);

            return QrValidationApiResult.Ok(verification, raw, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QrValidationApiResult.Error("La API de firma excedio el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return QrValidationApiResult.Error("No fue posible conectar con la API de firma.");
        }
        catch (JsonException)
        {
            return QrValidationApiResult.Error("La API devolvio una respuesta de verificacion no valida.");
        }
    }

    private static StreamContent CreateFileContent(Stream stream, string? contentType)
    {
        var content = new StreamContent(stream);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        return content;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private Uri? GetBaseUri()
    {
        var baseUrl = FirstNonEmpty(
            _configuration["FirmaStampApi:BaseUrl"],
            _configuration["ApiFirma:BaseUrl"]);

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            ? baseUri
            : null;
    }

    private string? GetApiKey() =>
        FirstNonEmpty(
            _configuration["FirmaStampApi:ApiKey"],
            _configuration["ApiFirma:ApiKey"],
            _configuration["ApiSecurity:ApiKey"],
            _configuration["FirmaStampApi__ApiKey"],
            _configuration["ApiFirma__ApiKey"],
            _configuration["ApiSecurity__ApiKey"],
            Environment.GetEnvironmentVariable("FirmaStampApi__ApiKey"),
            Environment.GetEnvironmentVariable("ApiFirma__ApiKey"),
            Environment.GetEnvironmentVariable("ApiSecurity__ApiKey"));

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static string? ExtraerMensajeError(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            foreach (var name in new[] { "detail", "title", "message", "mensaje" })
            {
                if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                    return property.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var error in errors.EnumerateObject())
                {
                    if (error.Value.ValueKind == JsonValueKind.Array)
                    {
                        var first = error.Value.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.String)
                            return first.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return rawBody;
    }

    private static bool TryGetQrToken(
        string input,
        out string token,
        out string error)
    {
        token = string.Empty;
        error = string.Empty;
        input = input.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Pega el token o la URL contenida en el QR.";
            return false;
        }

        if (input.Length > 12_000)
        {
            error = "El valor del QR excede el tamano permitido.";
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "La URL del QR debe usar HTTP o HTTPS.";
                return false;
            }

            token = QueryHelpers.ParseQuery(uri.Query)["token"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                error = "La URL no contiene el parametro token.";
                return false;
            }
        }
        else
        {
            token = input;
        }

        if (token.Length > 8_000)
        {
            error = "El token del QR excede el tamano permitido.";
            return false;
        }

        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record FirmaStampApiFile(
    byte[] Content,
    string FileName,
    string ContentType);

public sealed record FirmaStampApiResult(
    bool Success,
    string Message,
    byte[]? Pdf,
    string? ContentType = null,
    string? OriginalSha256 = null,
    string? OutputSha256 = null,
    string? RawBody = null,
    int? HttpStatusCode = null)
{
    public static FirmaStampApiResult Ok(
        byte[] pdf,
        string contentType,
        string? originalSha256,
        string? outputSha256,
        int? httpStatusCode) =>
        new(true, string.Empty, pdf, contentType, originalSha256, outputSha256, null, httpStatusCode);

    public static FirmaStampApiResult Error(string message, string? rawBody = null, int? httpStatusCode = null) =>
        new(false, message, null, null, null, null, rawBody, httpStatusCode);
}

public sealed record QrVerificationApiResponse(
    bool Valido,
    int Version,
    JsonElement Datos,
    string DocumentoOriginalSha256,
    DateTimeOffset FirmadoEnUtc);

public sealed record QrValidationApiResult(
    bool Success,
    string Message,
    QrVerificationApiResponse? Verification,
    string? RawBody = null,
    int? HttpStatusCode = null)
{
    public static QrValidationApiResult Ok(
        QrVerificationApiResponse verification,
        string? rawBody,
        int? httpStatusCode) =>
        new(true, string.Empty, verification, rawBody, httpStatusCode);

    public static QrValidationApiResult Error(string message, string? rawBody = null, int? httpStatusCode = null) =>
        new(false, message, null, rawBody, httpStatusCode);
}
