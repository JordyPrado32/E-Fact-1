using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Simetric.DTOs.ESign;
using Simetric.Models;

namespace Simetric.Services.ESign;

public sealed class BesPrecompraService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BesPrecompraService> _logger;

    public BesPrecompraService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<BesPrecompraService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["BESPrecompra:BaseUrl"]) &&
        (!RequireAuthentication ||
         (!string.IsNullOrWhiteSpace(_configuration["BESPrecompra:Username"]) &&
          !string.IsNullOrWhiteSpace(_configuration["BESPrecompra:Password"])));

    public async Task<IReadOnlyList<BesProductoDto>> ObtenerProductosAsync(CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        return await SendAsync<List<BesProductoDto>>(HttpMethod.Get, GetPath("ProductsPath", "/products"), token, null, cancellationToken)
            ?? [];
    }

    public async Task<IReadOnlyList<BesStakeholderProductDto>> ObtenerProductosStakeholderAsync(string? stakeholderUuid = null, CancellationToken cancellationToken = default)
    {
        stakeholderUuid ??= _configuration["BESPrecompra:StakeholderUuid"];
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
        CancellationToken cancellationToken = default)
    {
        var token = await ObtenerTokenSiAplicaAsync(cancellationToken);
        var path = GetPath("CreateCertificateRequestPath", "/api/certificateRequests");
        var sendAsArray = bool.TryParse(_configuration["BESPrecompra:SendCreatePayloadAsArray"], out var value)
            ? value
            : true;

        var payload = sendAsArray ? JsonSerializer.Serialize(new[] { request }, JsonOptions) : JsonSerializer.Serialize(request, JsonOptions);
        using var httpRequest = BuildRequest(HttpMethod.Post, path, token, null);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var location = response.Headers.Location?.ToString();

        return new BesCreateCertificateResponseDto
        {
            Success = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Location = location,
            ResponseBody = PrettyJson(body),
            ErrorMessage = response.IsSuccessStatusCode
                ? null
                : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
            Uuid = ExtractUuidFromLocation(location)
        };
    }

    public async Task<string> ResolverProductoUuidAsync(UsuSolicitudFirma solicitud, CancellationToken cancellationToken = default)
    {
        var mappingKey = $"{(solicitud.SolTipoPersona ?? "NATURAL").Trim().ToUpperInvariant()}|{(solicitud.SolFormatoFirma ?? string.Empty).Trim().ToUpperInvariant()}|{(solicitud.SolVigencia ?? string.Empty).Trim().ToUpperInvariant()}";
        var explicitMapping = _configuration[$"BESPrecompra:ProductMappings:{mappingKey}"];
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
            throw new InvalidOperationException($"No se encontro un producto BES activo para la combinacion {mappingKey}. Configura BESPrecompra:ProductMappings:{mappingKey}.");
        }

        return match;
    }

    private bool RequireAuthentication =>
        !bool.TryParse(_configuration["BESPrecompra:RequireAuthentication"], out var requireAuthentication) ||
        requireAuthentication;

    private async Task<string?> ObtenerTokenSiAplicaAsync(CancellationToken cancellationToken)
    {
        if (!RequireAuthentication)
        {
            return null;
        }

        return await AutenticarAsync(cancellationToken);
    }

    private async Task<string> AutenticarAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("La integracion BES Precompra no esta configurada. Revisa BESPrecompra:BaseUrl y, si aplica, Username y Password.");
        }

        var request = new
        {
            username = _configuration["BESPrecompra:Username"],
            password = _configuration["BESPrecompra:Password"]
        };

        using var httpRequest = BuildRequest(HttpMethod.Post, GetPath("LoginPath", "/auth/login"), bearerToken: null, query: null);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("BES auth fallo. Status: {Status}. Body: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"No fue posible autenticarse contra BES. HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var auth = JsonSerializer.Deserialize<BesAuthResponseDto>(body, JsonOptions);
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            throw new InvalidOperationException("BES devolvio una autenticacion sin token.");
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
            _logger.LogWarning("BES request fallo. Method: {Method}. Path: {Path}. Status: {Status}. Body: {Body}", method, path, (int)response.StatusCode, body);
            throw new InvalidOperationException($"BES respondio HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
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
        var baseUrl = (_configuration["BESPrecompra:BaseUrl"] ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("No se ha configurado BESPrecompra:BaseUrl.");
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
        => _configuration[$"BESPrecompra:{key}"] ?? fallback;

    private static string ObtenerVigenciaTextoBusqueda(string? vigencia)
    {
        var normalized = (vigencia ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "30 DIAS" => "30",
            "1 AÑO" or "1 AÃ‘O" => "1 año",
            "2 AÑOS" or "2 AÃ‘OS" => "2 años",
            "3 AÑOS" or "3 AÃ‘OS" => "3 años",
            "4 AÑOS" or "4 AÃ‘OS" => "4 años",
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
}
