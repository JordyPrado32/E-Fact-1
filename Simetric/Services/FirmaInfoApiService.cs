using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simetric.Services;

public sealed class FirmaInfoApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public FirmaInfoApiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<FirmaInfoApiResult> ConsultarAsync(
        string rutaFirma,
        string passwordFirma,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["FirmaInfoApi:BaseUrl"]?.Trim();
        var apiKey = _configuration["FirmaInfoApi:ApiKey"]?.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return FirmaInfoApiResult.Error("La URL del servicio de validacion de firma no esta configurada.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return FirmaInfoApiResult.Error("La clave del servicio de validacion de firma no esta configurada.");

        var endpoint = new Uri(baseUri, "api/firma/info");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(new FirmaInfoApiRequest(rutaFirma, passwordFirma));

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var mensaje = ExtraerMensaje(rawBody)
                    ?? $"El servicio de firma respondio con estado {(int)response.StatusCode}.";
                return FirmaInfoApiResult.Error(mensaje);
            }

            var info = JsonSerializer.Deserialize<FirmaInfoApiResponse>(
                rawBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            return info is null
                ? FirmaInfoApiResult.Error("El servicio de firma devolvio una respuesta vacia o invalida.")
                : FirmaInfoApiResult.Ok(info);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FirmaInfoApiResult.Error("El servicio de validacion de firma excedio el tiempo de espera.");
        }
        catch (HttpRequestException)
        {
            return FirmaInfoApiResult.Error("No fue posible conectar con el servicio de validacion de firma.");
        }
        catch (JsonException)
        {
            return FirmaInfoApiResult.Error("El servicio de firma devolvio una respuesta no valida.");
        }
    }

    private static string? ExtraerMensaje(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            foreach (var nombre in new[] { "mensaje", "message", "title" })
            {
                if (root.TryGetProperty(nombre, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private sealed record FirmaInfoApiRequest(
        [property: JsonPropertyName("RutaFirma")] string RutaFirma,
        [property: JsonPropertyName("PasswordFirma")] string PasswordFirma);
}

public sealed record FirmaInfoApiResult(
    bool Success,
    string Message,
    FirmaInfoApiResponse? Info)
{
    public static FirmaInfoApiResult Ok(FirmaInfoApiResponse info) =>
        new(true, string.Empty, info);

    public static FirmaInfoApiResult Error(string message) =>
        new(false, message, null);
}

public sealed class FirmaInfoApiResponse
{
    public bool EsValida { get; set; }
    public string? EstadoVigencia { get; set; }
    public string? Mensaje { get; set; }
    public string? NombreTitular { get; set; }
    public string? Ruc { get; set; }
    public string? Cedula { get; set; }
    public DateTimeOffset? FechaEmision { get; set; }
    public DateTimeOffset? FechaExpiracion { get; set; }
    public int DiasRestantes { get; set; }
    public bool TieneClavePrivada { get; set; }
}
