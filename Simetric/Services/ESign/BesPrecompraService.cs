using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Simetric.DTOs.ESign;
using Simetric.Models;

namespace Simetric.Services.ESign;

public sealed class UanatacaApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<UanatacaApiService> _logger;

    public UanatacaApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<UanatacaApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["UanatacaApi:BaseUrl"]);

    public async Task<IReadOnlyList<BesProductoDto>> ObtenerProductosAsync(CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        return await SendAsync<List<BesProductoDto>>(HttpMethod.Get, GetPath("ProductsPath", "/products"), token, null, cancellationToken)
            ?? [];
    }

    public async Task<IReadOnlyList<BesStakeholderProductDto>> ObtenerProductosStakeholderAsync(string? stakeholderUuid = null, CancellationToken cancellationToken = default)
    {
        stakeholderUuid ??= _configuration["UanatacaApi:StakeholderUuid"];
        if (string.IsNullOrWhiteSpace(stakeholderUuid))
        {
            return [];
        }

        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        var path = GetPath("StakeholderProductsPathTemplate", "/stakeholderProducts/{stakeholderUuid}")
            .Replace("{stakeholderUuid}", Uri.EscapeDataString(stakeholderUuid), StringComparison.OrdinalIgnoreCase);

        return await SendAsync<List<BesStakeholderProductDto>>(HttpMethod.Get, path, token, null, cancellationToken)
            ?? [];
    }

    public async Task<decimal?> ObtenerSaldoAsync(CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        var raw = await SendRawAsync(HttpMethod.Get, GetPath("BalancePath", "/uanacredits/balance"), token, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var balance)
            ? balance
            : null;
    }

    public async Task<IReadOnlyList<BesCertificateRequestDto>> BuscarSolicitudesAsync(
        string? q = null,
        string? status = null,
        string? uuid = null,
        CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        var query = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query["q"] = q;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query["status"] = status;
        }

        if (!string.IsNullOrWhiteSpace(uuid))
        {
            query["uuid"] = uuid;
        }

        return await SendAsync<List<BesCertificateRequestDto>>(
            HttpMethod.Get,
            GetPath("CertificateRequestsPath", "/certificateRequests"),
            token,
            query,
            cancellationToken) ?? [];
    }

    public async Task<BesCreateCertificateResponseDto> CrearSolicitudAsync(
        BesCreateCertificateRequestDto request,
        int? solicitudId = null,
        CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        var path = GetPath("CreateCertificateRequestPath", "/api/certificateRequests");
        var sendAsArray = bool.TryParse(_configuration["UanatacaApi:SendCreatePayloadAsArray"], out var value)
            ? value
            : true;

        var payload = sendAsArray ? JsonSerializer.Serialize(new[] { request }, JsonOptions) : JsonSerializer.Serialize(request, JsonOptions);
        await GuardarJsonSolicitudAsync(payload, solicitudId, cancellationToken);
        using var httpRequest = BuildRequest(HttpMethod.Post, path, token, null);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var location = response.Headers.Location?.ToString();
        var responseBody = BuildResponseDiagnostic(response, body);

        var uuid = ExtractUuidFromLocation(location) ?? ExtractUuidFromBody(body);

        _logger.LogInformation(
            "Uanataca POST creación. Path: {Path}. HTTP: {StatusCode}. UUID recibido: {Uuid}. Location: {Location}. Respuesta: {ResponseBody}",
            path,
            (int)response.StatusCode,
            uuid ?? "(no recibido)",
            location ?? "(none)",
            response.IsSuccessStatusCode ? "(omitida)" : TruncateLog(responseBody, 4000));

        return new BesCreateCertificateResponseDto
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Location = location,
            ResponseBody = responseBody,
            ErrorMessage = response.IsSuccessStatusCode
                ? null
                : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
            Uuid = uuid
        };
    }

    public async Task<string> ResolverProductoUuidAsync(UsuSolicitudFirma solicitud, CancellationToken cancellationToken = default)
    {
        var mappingKey = $"{(solicitud.SolTipoPersona ?? "NATURAL").Trim().ToUpperInvariant()}|{(solicitud.SolFormatoFirma ?? string.Empty).Trim().ToUpperInvariant()}|{(solicitud.SolVigencia ?? string.Empty).Trim().ToUpperInvariant()}";
        var explicitMapping = _configuration[$"UanatacaApi:ProductMappings:{mappingKey}"];
        if (!string.IsNullOrWhiteSpace(explicitMapping))
        {
            return explicitMapping;
        }

        var stakeholderProducts = await ObtenerProductosStakeholderAsync(cancellationToken: cancellationToken);
        var products = await ObtenerProductosAsync(cancellationToken);

        var vigenciaTexto = ObtenerVigenciaTextoBusqueda(solicitud.SolVigencia);
        var requiereEmpresa = string.Equals(solicitud.SolTipoPersona, "JURIDICA", StringComparison.OrdinalIgnoreCase);

        var match = (from stakeholderProduct in stakeholderProducts
                     join product in products on stakeholderProduct.ProductUuid equals product.Uuid
                     where stakeholderProduct.Active
                        && product.Active
                        && product.Name.Contains("ARCHIVO", StringComparison.OrdinalIgnoreCase)
                        && product.Name.Contains(vigenciaTexto, StringComparison.OrdinalIgnoreCase)
                        && (requiereEmpresa
                            ? product.Name.Contains("EMPRESA", StringComparison.OrdinalIgnoreCase)
                              || product.Name.Contains("REPRESENTANTE", StringComparison.OrdinalIgnoreCase)
                              || product.Name.Contains("MIEMBRO", StringComparison.OrdinalIgnoreCase)
                            : true)
                     orderby stakeholderProduct.Price, product.Price
                     select product.Uuid)
                    .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(match))
        {
            throw new InvalidOperationException($"No se encontró un producto Uanataca activo para la combinación {mappingKey}. Configura UanatacaApi:ProductMappings:{mappingKey}.");
        }

        return match;
    }

    private bool RequireAuthentication =>
        bool.TryParse(_configuration["UanatacaApi:RequireAuthentication"], out var requireAuthentication) &&
        requireAuthentication;

    private async Task<string?> ObtenerTokenSiAplicaAsync(CancellationToken cancellationToken)
    {
        if (!RequireAuthentication)
        {
            _logger.LogDebug("Uanataca autenticación omitida: RequireAuthentication=false.");
            return null;
        }

        return await AutenticarAsync(cancellationToken);
    }

    private async Task<string> AutenticarAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("La API de Uanataca no está configurada. Revisa UanatacaApi:BaseUrl y las credenciales requeridas.");
        }

        var request = new
        {
            username = _configuration["UanatacaApi:Username"],
            password = _configuration["UanatacaApi:Password"]
        };

        using var httpRequest = BuildRequest(HttpMethod.Post, GetPath("LoginPath", "/auth/login"), bearerToken: null, query: null);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Uanataca auth fallo. Status: {Status}. Body: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"No fue posible autenticarse contra Uanataca. HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var auth = JsonSerializer.Deserialize<BesAuthResponseDto>(body, JsonOptions);
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            throw new InvalidOperationException("Uanataca devolvió una autenticación sin token.");
        }

        return auth.AccessToken;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        string bearerToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        var raw = await SendRawAsync(method, path, bearerToken, query, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private async Task<string?> SendRawAsync(
        HttpMethod method,
        string path,
        string bearerToken,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(method, path, bearerToken, query);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Uanataca request fallo. Method: {Method}. Path: {Path}. Status: {Status}. Body: {Body}", method, path, (int)response.StatusCode, body);
            throw new InvalidOperationException($"Uanataca respondió HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return body;
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        string? bearerToken,
        IReadOnlyDictionary<string, string?>? query)
    {
        var url = BuildUrl(path, query);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    private string BuildUrl(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var baseUrl = (_configuration["UanatacaApi:BaseUrl"] ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("No se ha configurado UanatacaApi:BaseUrl.");
        }

        var url = $"{baseUrl}{(path.StartsWith('/') ? path : "/" + path)}";
        if (query is not { Count: > 0 })
        {
            return url;
        }

        var validPairs = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}");

        var queryString = string.Join("&", validPairs);
        return string.IsNullOrWhiteSpace(queryString) ? url : $"{url}?{queryString}";
    }

    private string GetPath(string key, string fallback)
        => _configuration[$"UanatacaApi:{key}"] ?? fallback;

    private static string ObtenerVigenciaTextoBusqueda(string? vigencia)
    {
        var normalized = (vigencia ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Replace("Ñ", "N", StringComparison.OrdinalIgnoreCase);

        return normalized switch
        {
            "7 DIAS" => "7",
            "30 DIAS" => "30",
            "1 ANO" or "1 ANIO" => "1 año",
            "2 ANOS" or "2 ANIOS" => "2 años",
            "3 ANOS" or "3 ANIOS" => "3 años",
            "4 ANOS" or "4 ANIOS" => "4 años",
            "5 ANOS" or "5 ANIOS" => "5 años",
            _ => normalized
        };
    }

    private static string? ExtractUuidFromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        return location
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
    }

    private async Task GuardarJsonSolicitudAsync(
        string payload,
        int? solicitudId,
        CancellationToken cancellationToken)
    {
        var enabled = !bool.TryParse(_configuration["UanatacaApi:SaveRequestJson"], out var saveRequestJson)
            || saveRequestJson;
        if (!enabled)
        {
            return;
        }

        try
        {
            var configuredDirectory = _configuration["UanatacaApi:RequestJsonDirectory"];
            var directory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", "uanataca-requests")
                : Path.IsPathRooted(configuredDirectory)
                    ? configuredDirectory
                    : Path.Combine(_hostEnvironment.ContentRootPath, configuredDirectory);

            Directory.CreateDirectory(directory);
            var suffix = solicitudId.HasValue ? $"sol-{solicitudId.Value}" : "sin-sol-id";
            var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{suffix}.json";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, payload, Encoding.UTF8, cancellationToken);
            _logger.LogInformation("JSON Uanataca guardado. SolId: {SolId}. Archivo: {Path}", solicitudId, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "No se pudo guardar el JSON de Uanataca. SolId: {SolId}", solicitudId);
        }
    }

    private static string? ExtractUuidFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return FindIdentifier(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FindIdentifier(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "uuid", "id", "requestUuid", "certificateRequestUuid" })
            {
                if (element.TryGetProperty(propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindIdentifier(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindIdentifier(item);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? PrettyJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static string BuildResponseDiagnostic(HttpResponseMessage response, string? body)
    {
        var formattedBody = PrettyJson(body);
        if (!string.IsNullOrWhiteSpace(formattedBody))
        {
            return formattedBody;
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "(sin Content-Type)";
        var contentLength = response.Content.Headers.ContentLength?.ToString() ?? "desconocido";
        return $"(respuesta HTTP sin cuerpo; Content-Type: {contentType}; Content-Length: {contentLength})";
    }

    private static string TruncateLog(string? value, int maxLength)
    {
        var text = value ?? string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
