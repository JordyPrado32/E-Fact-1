using System.Globalization;
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
        IBrowserFile logo,
        IBrowserFile certificado,
        string passwordCertificado,
        string datos,
        int pagina,
        double xMm,
        double yMm,
        double anchoMm,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["FirmaStampApi:BaseUrl"]?.Trim();
        var apiKey = _configuration["FirmaStampApi:ApiKey"]?.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return FirmaStampApiResult.Error("La URL de la API de estampado no esta configurada.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return FirmaStampApiResult.Error("La API key de estampado no esta configurada.");

        await using var pdfStream = pdf.OpenReadStream(MaxFileBytes, cancellationToken);
        await using var logoStream = logo.OpenReadStream(MaxFileBytes, cancellationToken);
        await using var certificadoStream = certificado.OpenReadStream(MaxFileBytes, cancellationToken);

        using var form = new MultipartFormDataContent();
        using var pdfContent = CreateFileContent(pdfStream, pdf.ContentType);
        using var logoContent = CreateFileContent(logoStream, logo.ContentType);
        using var certificadoContent = CreateFileContent(certificadoStream, certificado.ContentType);

        form.Add(pdfContent, "pdf", pdf.Name);
        form.Add(logoContent, "logo", logo.Name);
        form.Add(certificadoContent, "certificado", certificado.Name);
        form.Add(new StringContent(passwordCertificado), "passwordCertificado");
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

    private static StreamContent CreateFileContent(Stream stream, string? contentType)
    {
        var content = new StreamContent(stream);
        if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        return content;
    }

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
}

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
