using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Simetric.Services.ESign;

public sealed class FirmaStampApiService
{
    private const long MaxFileBytes = 15 * 1024 * 1024;
    private static readonly SemaphoreSlim DiagnosticLogLock = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FirmaStampApiService> _logger;
    private readonly IWebHostEnvironment _hostEnvironment;

    public FirmaStampApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FirmaStampApiService> logger,
        IWebHostEnvironment hostEnvironment)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    public async Task<FirmaStampApiResult> EstamparAsync(
        IBrowserFile pdf,
        FirmaStampApiFile certificado,
        string clave,
        string? razon,
        string? ubicacion,
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

        if (!TryValidateCertificate(certificado.Content, clave, out var certificateValidationError))
            return FirmaStampApiResult.Error(certificateValidationError);

        await using var pdfStream = pdf.OpenReadStream(MaxFileBytes, cancellationToken);

        using var form = new MultipartFormDataContent();
        using var pdfContent = CreateFileContent(pdfStream, pdf.ContentType);
        using var certificadoContent = CreateFileContent(certificado.Content, "application/x-pkcs12");
        var certificadoFileName = NormalizeCertificateFileName(certificado.FileName);

        form.Add(pdfContent, "pdf", pdf.Name);
        form.Add(certificadoContent, "certificado", certificadoFileName);
        form.Add(new StringContent(clave), "clave");
        if (!string.IsNullOrWhiteSpace(razon))
            form.Add(new StringContent(razon.Trim()), "razon");
        if (!string.IsNullOrWhiteSpace(ubicacion))
            form.Add(new StringContent(ubicacion.Trim()), "ubicacion");
        form.Add(new StringContent(pagina.ToString(CultureInfo.InvariantCulture)), "pagina");
        form.Add(new StringContent(xMm.ToString(CultureInfo.InvariantCulture)), "xMm");
        form.Add(new StringContent(yMm.ToString(CultureInfo.InvariantCulture)), "yMm");
        form.Add(new StringContent(anchoMm.ToString(CultureInfo.InvariantCulture)), "anchoMm");

        var endpoint = BuildEndpointUri(baseUri, "EstamparPath", "api/documentos/estampar");
        var correlationId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        request.Content = form;

        _logger.LogInformation(
            "Enviando firma {CorrelationId} a {Endpoint}. Campos: pdf, certificado, clave. PDF bytes: {PdfBytes}. P12 bytes: {P12Bytes}. P12 SHA256: {P12Hash}. Clave presente: {ClavePresente}. Longitud de clave: {ClaveLength}.",
            correlationId,
            endpoint,
            pdf.Size,
            certificado.Content.Length,
            Convert.ToHexString(SHA256.HashData(certificado.Content)),
            !string.IsNullOrEmpty(clave),
            clave.Length);

        await RegistrarDiagnosticoAsync(new
        {
            Evento = "Solicitud",
            CorrelationId = correlationId,
            Endpoint = endpoint.ToString(),
            Pdf = new { pdf.Name, pdf.Size, pdf.ContentType },
            Certificado = new
            {
                certificado.FileName,
                MultipartFileName = certificadoFileName,
                Size = certificado.Content.Length,
                ContentType = certificadoContent.Headers.ContentType?.ToString(),
                ContentLength = certificadoContent.Headers.ContentLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(certificado.Content))
            },
            ClaveRecibida = !string.IsNullOrEmpty(clave),
            ClaveLongitud = clave.Length,
            Posicion = new { pagina, xMm, yMm, anchoMm },
            Razon = razon,
            Ubicacion = ubicacion
        });
        await RegistrarAperturaLocalCertificadoAsync(
            certificado,
            clave,
            correlationId);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var requestId = TryGetHeader(response, "X-Request-ID");

            if (!response.IsSuccessStatusCode)
            {
                var raw = System.Text.Encoding.UTF8.GetString(bytes);
                var mensaje = InterpretarErrorEstampado(raw, (int)response.StatusCode)
                    ?? $"La API de firma respondio con estado {(int)response.StatusCode}.";

                _logger.LogWarning(
                    "La API rechazo el estampado {CorrelationId}. Request ID: {RequestId}. Estado: {StatusCode}. P12 SHA256: {P12Hash}. Mensaje: {Mensaje}",
                    correlationId,
                    requestId,
                    (int)response.StatusCode,
                    Convert.ToHexString(SHA256.HashData(certificado.Content)),
                    mensaje);

                await RegistrarDiagnosticoAsync(new
                {
                    Evento = "RespuestaError",
                    CorrelationId = correlationId,
                    RequestId = requestId,
                    EstadoHttp = (int)response.StatusCode,
                    RazonHttp = response.ReasonPhrase,
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    MensajeInterpretado = mensaje,
                    RespuestaApi = raw
                });

                return FirmaStampApiResult.Error(mensaje, raw, (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";
            var originalHash = TryGetHeader(response, "X-Documento-Original-SHA256");
            var outputHash = TryGetHeader(response, "X-Documento-Salida-SHA256");

            _logger.LogInformation(
                "Estampado aprobado por la API {CorrelationId}. Request ID: {RequestId}. Estado: {StatusCode}. P12 SHA256: {P12Hash}.",
                correlationId,
                requestId,
                (int)response.StatusCode,
                Convert.ToHexString(SHA256.HashData(certificado.Content)));

            await RegistrarDiagnosticoAsync(new
            {
                Evento = "RespuestaExitosa",
                CorrelationId = correlationId,
                RequestId = requestId,
                EstadoHttp = (int)response.StatusCode,
                ContentType = contentType,
                BytesRespuesta = bytes.Length,
                OriginalSha256 = originalHash,
                SalidaSha256 = outputHash
            });

            return FirmaStampApiResult.Ok(bytes, contentType, originalHash, outputHash, (int)response.StatusCode);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RegistrarDiagnosticoAsync(new
            {
                Evento = "Timeout",
                CorrelationId = correlationId,
                Excepcion = exception.GetType().FullName,
                exception.Message
            });
            return FirmaStampApiResult.Error("La API de firma excedio el tiempo de espera.");
        }
        catch (HttpRequestException exception)
        {
            await RegistrarDiagnosticoAsync(new
            {
                Evento = "ErrorConexion",
                CorrelationId = correlationId,
                Excepcion = exception.GetType().FullName,
                exception.Message,
                InnerException = exception.InnerException?.Message
            });
            return FirmaStampApiResult.Error("No fue posible conectar con la API de firma.");
        }
    }

    private async Task RegistrarDiagnosticoAsync(object detalle)
    {
        if (string.Equals(
                _configuration["FirmaStampApi:DiagnosticLogEnabled"],
                "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var directory = Path.Combine(
                _hostEnvironment.ContentRootPath,
                "App_Data",
                "logs");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(
                directory,
                $"firma-api-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = JsonSerializer.Serialize(new
            {
                FechaUtc = DateTimeOffset.UtcNow,
                Detalle = detalle
            }, JsonOptions) + Environment.NewLine;

            await DiagnosticLogLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(path, line);
            }
            finally
            {
                DiagnosticLogLock.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "No se pudo guardar el diagnostico de FirmaStampApi en App_Data/logs.");
        }
    }

    private async Task RegistrarAperturaLocalCertificadoAsync(
        FirmaStampApiFile certificado,
        string clave,
        string correlationId)
    {
        try
        {
            using var certificate = new X509Certificate2(
                certificado.Content,
                clave,
                X509KeyStorageFlags.EphemeralKeySet);

            await RegistrarDiagnosticoAsync(new
            {
                Evento = "AperturaLocalCertificadoExitosa",
                CorrelationId = correlationId,
                certificate.Thumbprint,
                certificate.HasPrivateKey,
                certificate.NotBefore,
                certificate.NotAfter,
                certificate.Subject,
                certificate.Issuer
            });
        }
        catch (Exception exception)
        {
            await RegistrarDiagnosticoAsync(new
            {
                Evento = "AperturaLocalCertificadoError",
                CorrelationId = correlationId,
                Excepcion = exception.GetType().FullName,
                exception.HResult,
                exception.Message,
                InnerException = exception.InnerException?.Message
            });
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
            BuildEndpointUri(baseUri, "QrValidationPath", "api/verificaciones/qr", new Dictionary<string, string?>
            {
                ["token"] = token
            }));

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

    public async Task<PdfSignatureValidationApiResult> ValidarFirmaPdfAsync(
        IBrowserFile pdf,
        CancellationToken cancellationToken = default)
    {
        var baseUri = GetBaseUri();
        var apiKey = GetApiKey();

        if (baseUri is null)
            return PdfSignatureValidationApiResult.Error("La URL de la API de estampado no esta configurada.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return PdfSignatureValidationApiResult.Error("La API key de estampado no esta configurada.");

        await using var pdfStream = pdf.OpenReadStream(10 * 1024 * 1024, cancellationToken);
        using var form = new MultipartFormDataContent();
        using var pdfContent = CreateFileContent(pdfStream, pdf.ContentType);

        form.Add(pdfContent, "pdf", pdf.Name);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(baseUri, "ValidarFirmaPath", "api/documentos/validar-firma"));
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Content = form;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var mensaje = ExtraerMensajeError(raw)
                    ?? $"La API de firma respondio con estado {(int)response.StatusCode}.";

                return PdfSignatureValidationApiResult.Error(mensaje, raw, (int)response.StatusCode);
            }

            var validation = ParsePdfSignatureValidation(raw);

            return validation is null
                ? PdfSignatureValidationApiResult.Error("La API no devolvio un resultado de validacion valido.", raw, (int)response.StatusCode)
                : PdfSignatureValidationApiResult.Ok(validation, raw, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PdfSignatureValidationApiResult.Error("La API de firma excedio el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return PdfSignatureValidationApiResult.Error("No fue posible conectar con la API de firma.");
        }
        catch (JsonException)
        {
            return PdfSignatureValidationApiResult.Error("La API devolvio una respuesta de validacion no valida.");
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

    private static ByteArrayContent CreateFileContent(byte[] contentBytes, string contentType)
    {
        var content = new ByteArrayContent(contentBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.ContentLength = contentBytes.Length;
        return content;
    }

    private static string NormalizeCertificateFileName(string? fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            return $"certificado-{Guid.NewGuid():N}.p12";

        return Path.GetExtension(safeName).Equals(".pfx", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(safeName).Equals(".p12", StringComparison.OrdinalIgnoreCase)
            ? safeName
            : $"{safeName}.p12";
    }

    private static bool TryValidateCertificate(byte[] content, string password, out string error)
    {
        error = string.Empty;

        if (content.Length == 0)
        {
            error = "El certificado configurado esta vacio.";
            return false;
        }

        try
        {
            using var certificate = new X509Certificate2(
                content,
                password,
                X509KeyStorageFlags.EphemeralKeySet);

            if (!certificate.HasPrivateKey)
            {
                error = "El certificado configurado no contiene una clave privada.";
                return false;
            }

            return true;
        }
        catch (CryptographicException)
        {
            error = "No se pudo abrir el certificado configurado con la clave registrada.";
            return false;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private Uri? GetBaseUri()
    {
        var baseUrl = FirstNonEmpty(
            _configuration["FirmaStampApi:BaseUrl"],
            _configuration["ApiFirma:BaseUrl"]);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return new Uri(baseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
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

    private Uri BuildEndpointUri(Uri baseUri, string configKey, string defaultPath, IDictionary<string, string?>? query = null)
    {
        var configuredPath = FirstNonEmpty(
            _configuration[$"FirmaStampApi:{configKey}"],
            _configuration[$"ApiFirma:{configKey}"]);

        var path = configuredPath ?? defaultPath;
        var uri = Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(baseUri, path);

        return query is null
            ? uri
            : new Uri(QueryHelpers.AddQueryString(uri.ToString(), query));
    }

    private static string? TryGetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    private static PdfSignatureValidationApiResponse? ParsePdfSignatureValidation(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = UnwrapPayload(document.RootElement);

        var firmas = new List<PdfSignatureDetailApiResponse>();
        if (TryGetProperty(root, out var firmasElement, "firmas", "signatures", "detalles", "signatureDetails") &&
            firmasElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var firma in firmasElement.EnumerateArray())
            {
                firmas.Add(ParsePdfSignatureDetail(firma));
            }
        }

        var cantidadFirmas = GetInt(root, firmas.Count, "cantidadFirmas", "signatureCount", "numeroFirmas", "totalFirmas");

        return new PdfSignatureValidationApiResponse(
            GetBool(root, false, "valido", "valid", "isValid", "esValida"),
            cantidadFirmas,
            GetBool(root, false, "documentoCompletoCubierto", "documentoCompleto", "documentCoverageValid", "coversWholeDocument"),
            firmas);
    }

    private static PdfSignatureDetailApiResponse ParsePdfSignatureDetail(JsonElement root)
    {
        var certificado = GetNestedObject(root, "certificado", "certificate", "cert");

        return new(
            GetBool(root, false, "valida", "valido", "valid", "isValid"),
            GetBool(root, false, "integridadValida", "integrityValid", "integridad", "documentIntegrityValid"),
            GetBool(root, false, "certificadoVigente", "certificateValid", "certificateInValidityPeriod", "certificadoValido") ||
                GetBool(certificado, false, "vigente", "valid", "isValid", "certificateValid"),
            GetBool(root, false, "cadenaConfiable", "chainTrusted", "cadenaValida", "trustedChain"),
            GetBool(root, false, "revocacionValida", "revocationValid", "revocationCheckValid"),
            GetString(root, "estadoRevocacion", "revocationStatus", "estadoRevocacionTexto") ?? "No disponible",
            GetBool(root, false, "cubreDocumentoCompleto", "coversWholeDocument", "documentoCompletoCubierto"),
            GetString(root, "firmante", "signer", "subjectName", "nombreTitular") ??
                GetString(certificado, "firmante", "subjectName", "nombreTitular", "commonName"),
            GetString(root, "emisor", "issuer", "issuerName") ??
                GetString(certificado, "emisor", "issuer", "issuerName"),
            GetString(root, "numeroSerie", "serialNumber", "serie") ??
                GetString(certificado, "numeroSerie", "serialNumber", "serie"),
            GetString(root, "huellaDigital", "thumbprint", "fingerprint") ??
                GetString(certificado, "huellaDigital", "thumbprint", "fingerprint"),
            GetDate(root, "fechaFirma", "signingTime", "signedAt", "fechaFirmado"),
            GetDate(root, "certificadoDesde", "validFrom", "notBefore", "fechaEmision", "fechaInicioVigencia") ??
                GetDate(certificado, "certificadoDesde", "validFrom", "notBefore", "fechaEmision", "fechaInicioVigencia", "desde"),
            GetDate(root, "certificadoHasta", "validTo", "notAfter", "fechaExpiracion", "fechaVencimiento", "fechaFinVigencia") ??
                GetDate(certificado, "certificadoHasta", "validTo", "notAfter", "fechaExpiracion", "fechaVencimiento", "fechaFinVigencia", "hasta"),
            GetString(root, "algoritmoHash", "hashAlgorithm", "digestAlgorithm"),
            GetNullableBool(root, "selloTiempoValido", "timestampValid", "timeStampValid"),
            GetDate(root, "fechaSelloTiempo", "timestampTime", "timeStampDate"),
            GetString(root, "autoridadSelloTiempo", "timestampAuthority", "tsa"),
            GetString(root, "error", "message", "mensaje"));
    }

    private static JsonElement UnwrapPayload(JsonElement root)
    {
        foreach (var name in new[] { "data", "datos", "result", "resultado", "payload" })
        {
            if (TryGetProperty(root, out var payload, name) && payload.ValueKind == JsonValueKind.Object)
                return payload;
        }

        return root;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => null
        };
    }

    private static string? GetString(JsonElement? element, params string[] names) =>
        element is JsonElement value ? GetString(value, names) : null;

    private static bool GetBool(JsonElement element, bool defaultValue, params string[] names) =>
        GetNullableBool(element, names) ?? defaultValue;

    private static bool GetBool(JsonElement? element, bool defaultValue, params string[] names) =>
        element is JsonElement value ? GetBool(value, defaultValue, names) : defaultValue;

    private static bool? GetNullableBool(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
            return null;

        if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            return value.GetBoolean();

        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static int GetInt(JsonElement element, int defaultValue, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names))
            return defaultValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            return parsed;

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : defaultValue;
    }

    private static DateTimeOffset? GetDate(JsonElement element, params string[] names)
    {
        var value = GetString(element, names);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetDate(JsonElement? element, params string[] names) =>
        element is JsonElement value ? GetDate(value, names) : null;

    private static JsonElement? GetNestedObject(JsonElement element, params string[] names) =>
        TryGetProperty(element, out var value, names) && value.ValueKind == JsonValueKind.Object
            ? value
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

    private static string? InterpretarErrorEstampado(string rawBody, int statusCode)
    {
        if (rawBody.Contains("PFX_LOAD_FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return rawBody.Contains("0x80070002", StringComparison.OrdinalIgnoreCase)
                ? "La API recibio el archivo P12, pero no pudo abrirlo en su almacenamiento temporal (PFX_LOAD_FAILED 0x80070002). El archivo y la clave fueron validados correctamente antes del envio."
                : "La API recibio el archivo P12, pero no pudo abrirlo internamente (PFX_LOAD_FAILED). El archivo y la clave fueron validados correctamente antes del envio.";
        }

        if (statusCode == 500 && rawBody.Contains("HTTP Error 500.30", StringComparison.OrdinalIgnoreCase))
        {
            return "El servicio remoto de firma no pudo iniciar. Intenta nuevamente cuando la API se encuentre disponible.";
        }

        return ExtraerMensajeError(rawBody);
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

public sealed record PdfSignatureValidationApiResponse(
    bool Valido,
    int CantidadFirmas,
    bool DocumentoCompletoCubierto,
    IReadOnlyList<PdfSignatureDetailApiResponse> Firmas);

public sealed record PdfSignatureDetailApiResponse(
    bool Valida,
    bool IntegridadValida,
    bool CertificadoVigente,
    bool CadenaConfiable,
    bool RevocacionValida,
    string EstadoRevocacion,
    bool CubreDocumentoCompleto,
    string? Firmante,
    string? Emisor,
    string? NumeroSerie,
    string? HuellaDigital,
    DateTimeOffset? FechaFirma,
    DateTimeOffset? CertificadoDesde,
    DateTimeOffset? CertificadoHasta,
    string? AlgoritmoHash,
    bool? SelloTiempoValido,
    DateTimeOffset? FechaSelloTiempo,
    string? AutoridadSelloTiempo,
    string? Error);

public sealed record PdfSignatureValidationApiResult(
    bool Success,
    string Message,
    PdfSignatureValidationApiResponse? Validation,
    string? RawBody = null,
    int? HttpStatusCode = null)
{
    public static PdfSignatureValidationApiResult Ok(
        PdfSignatureValidationApiResponse validation,
        string? rawBody,
        int? httpStatusCode) =>
        new(true, string.Empty, validation, rawBody, httpStatusCode);

    public static PdfSignatureValidationApiResult Error(string message, string? rawBody = null, int? httpStatusCode = null) =>
        new(false, message, null, rawBody, httpStatusCode);
}
