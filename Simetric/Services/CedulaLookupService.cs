using System.Collections.Concurrent;
using System.Text.Json;
using ConsultaDeRucApi.Services;

namespace Simetric.Services;

public sealed class CedulaLookupService
{
    private const string ApiKey = "ASDF234SEDFS21";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public CedulaLookupService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CedulaLookupResult> ConsultarAsync(string? identificacion, CancellationToken cancellationToken = default)
    {
        var identificacionNormalizada = new string((identificacion ?? string.Empty).Where(char.IsDigit).ToArray());
        if (identificacionNormalizada.Length is not (10 or 13))
            return CedulaLookupResult.Fail("La identificacion debe tener 10 digitos para cedula o 13 para RUC.");

        if (_cache.TryGetValue(identificacionNormalizada, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Result;

        var result = identificacionNormalizada.Length == 10
            ? await ConsultarCedulaInternaAsync(identificacionNormalizada, cancellationToken)
            : await ConsultarRucInternaAsync(identificacionNormalizada, cancellationToken);

        _cache[identificacionNormalizada] = new CacheEntry(result, DateTimeOffset.UtcNow.Add(CacheDuration));
        return result;
    }

    private async Task<CedulaLookupResult> ConsultarCedulaInternaAsync(string cedulaNormalizada, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"api/ConsultasDatos/ConsultaCedulaV3?Cedula={cedulaNormalizada}&Apikey={ApiKey}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return CedulaLookupResult.Fail($"La consulta externa devolvio HTTP {(int)response.StatusCode}.");

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
            return CedulaLookupResult.Fail("La API no devolvio contenido.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() < 3)
                return CedulaLookupResult.Fail("La respuesta de la API tiene un formato no esperado.");

            var registros = document.RootElement[2];
            if (registros.ValueKind != JsonValueKind.Array || registros.GetArrayLength() == 0)
                return CedulaLookupResult.NotFound("No se encontraron datos para la cedula consultada.");

            var item = registros[0];
            if (item.ValueKind != JsonValueKind.Object)
            {
                var mensajeItem = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                return CedulaLookupResult.Fail(
                    string.IsNullOrWhiteSpace(mensajeItem)
                        ? "La API devolvio un formato inesperado para la cedula consultada."
                        : mensajeItem);
            }

            return new CedulaLookupResult
            {
                Success = true,
                Found = true,
                Tipo = "cedula",
                Cedula = GetString(item, "cedula"),
                NombreCompleto = GetString(item, "nombre"),
                ObligadoLlevarContabilidad = "NO",
                EstadoCivil = GetString(item, "estadoCivil"),
                FechaNacimiento = GetString(item, "fechaNacimiento"),
                LugarNacimiento = GetString(item, "lugarNacimiento"),
                Sexo = GetString(item, "sexo"),
                CondicionCiudadano = GetString(item, "condicionCiudadano"),
                Mensaje = "Datos consultados correctamente."
            };
        }
        catch (JsonException)
        {
            return CedulaLookupResult.Fail("No fue posible interpretar la respuesta de la API.");
        }
    }

    private async Task<CedulaLookupResult> ConsultarRucInternaAsync(string rucNormalizado, CancellationToken cancellationToken)
    {
        try
        {
            var consulta = new CConsultaSri();
            var datos = await Task.Run(() => consulta.GetRucSri(rucNormalizado), cancellationToken);

            if (datos is null)
                return CedulaLookupResult.Fail("La consulta del RUC no devolvio informacion.");

            if (!string.IsNullOrWhiteSpace(datos.Error))
                return CedulaLookupResult.NotFound(datos.Error);

            var establecimiento = SeleccionarEstablecimiento(datos.personaEstablecimientos);
            var nombreComercial = Convert.ToString(datos.nombreComercial)?.Trim();
            var direccion = TextoSeguro(establecimiento?.direccionCompleta);

            return new CedulaLookupResult
            {
                Success = true,
                Found = true,
                Tipo = "ruc",
                Ruc = TextoSeguro(datos.numeroRuc),
                NombreCompleto = TextoSeguro(datos.razonSocial),
                RazonSocial = TextoSeguro(datos.razonSocial),
                NombreComercial = FirstNonEmpty(nombreComercial, datos.razonSocial),
                ObligadoLlevarContabilidad = NormalizarObligado(datos.obligado),
                TipoContribuyente = FirstNonEmpty(
                    datos.claseContribuyente,
                    datos.subtipoContribuyente,
                    datos.actividadContribuyente,
                    datos.personaSociedad),
                DireccionMatriz = direccion,
                DireccionEstablecimiento = direccion,
                NumeroEstablecimiento = TextoSeguro(establecimiento?.numeroEstablecimiento),
                EstadoEstablecimiento = TextoSeguro(establecimiento?.estado),
                Mensaje = "Datos consultados correctamente."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await ConsultarRucPorApiAsync(rucNormalizado, cancellationToken);
        }
    }

    private async Task<CedulaLookupResult> ConsultarRucPorApiAsync(string rucNormalizado, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"api/ConsultasDatosSri/RucSri?Ruc={rucNormalizado}&Apikey={ApiKey}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return CedulaLookupResult.Fail($"La consulta externa devolvio HTTP {(int)response.StatusCode}.");

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
            return CedulaLookupResult.Fail("La API no devolvio contenido.");

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var error = GetString(root, "error");

            if (!string.IsNullOrWhiteSpace(error))
                return CedulaLookupResult.NotFound(error);

            return new CedulaLookupResult
            {
                Success = true,
                Found = true,
                Tipo = "ruc",
                Ruc = GetString(root, "numeroRuc"),
                NombreCompleto = GetString(root, "razonSocial"),
                RazonSocial = GetString(root, "razonSocial"),
                NombreComercial = FirstNonEmpty(
                    GetString(root, "nombreComercial"),
                    GetString(root, "nombreFantasiaComercial"),
                    GetString(root, "razonSocial")),
                ObligadoLlevarContabilidad = NormalizarObligado(
                    FirstNonEmpty(
                        GetString(root, "obligadoLlevarContabilidad"),
                        GetString(root, "obligado"))),
                TipoContribuyente = FirstNonEmpty(
                    GetString(root, "tipoContribuyente"),
                    GetString(root, "claseContribuyente"),
                    GetString(root, "subtipoContribuyente")),
                DireccionMatriz = GetDireccionEstablecimientoSeleccionado(root),
                DireccionEstablecimiento = GetDireccionEstablecimientoSeleccionado(root),
                NumeroEstablecimiento = GetNumeroEstablecimientoSeleccionado(root),
                EstadoEstablecimiento = GetEstadoEstablecimientoSeleccionado(root),
                Mensaje = "Datos consultados correctamente."
            };
        }
        catch (JsonException)
        {
            return CedulaLookupResult.Fail("No fue posible interpretar la respuesta de la API.");
        }
    }

    private static string? GetString(JsonElement item, string propertyName)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;
        }

        if (!item.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static string? GetDireccionEstablecimientoSeleccionado(JsonElement root)
        => FirstNonEmpty(
            GetString(SeleccionarEstablecimiento(root), "direccionCompleta"),
            GetString(root, "direccionCompleta"),
            GetString(root, "direccion"),
            GetString(root, "direccionMatriz"));

    private static string? GetNumeroEstablecimientoSeleccionado(JsonElement root)
        => FirstNonEmpty(
            GetString(SeleccionarEstablecimiento(root), "numeroEstablecimiento"),
            GetString(root, "numeroEstablecimiento"),
            GetString(root, "establecimiento"));

    private static string? GetEstadoEstablecimientoSeleccionado(JsonElement root)
        => GetString(SeleccionarEstablecimiento(root), "estado");

    private static JsonElement SeleccionarEstablecimiento(JsonElement root)
    {
        if (!root.TryGetProperty("personaEstablecimientos", out var establecimientos) ||
            establecimientos.ValueKind != JsonValueKind.Array ||
            establecimientos.GetArrayLength() == 0)
        {
            return default;
        }

        foreach (var item in establecimientos.EnumerateArray())
        {
            var estado = GetString(item, "estado");
            if (!string.IsNullOrWhiteSpace(estado) &&
                (estado.Contains("ABIER", StringComparison.OrdinalIgnoreCase) ||
                 estado.Contains("ACT", StringComparison.OrdinalIgnoreCase)))
            {
                return item;
            }
        }

        return establecimientos[0];
    }

    private static PersonaEstablecimientosRuc? SeleccionarEstablecimiento(List<PersonaEstablecimientosRuc>? establecimientos)
    {
        if (establecimientos is null || establecimientos.Count == 0)
            return null;

        return establecimientos.FirstOrDefault(item =>
                   (item.estado ?? string.Empty).Contains("ABIER", StringComparison.OrdinalIgnoreCase) ||
                   (item.estado ?? string.Empty).Contains("ACT", StringComparison.OrdinalIgnoreCase))
               ?? establecimientos.FirstOrDefault();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string TextoSeguro(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim();

    private static string NormalizarObligado(string? value)
    {
        var normalizado = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizado))
            return "NO";

        if (normalizado.Equals("S", StringComparison.OrdinalIgnoreCase) ||
            normalizado.Equals("SI", StringComparison.OrdinalIgnoreCase) ||
            normalizado.Equals("Si", StringComparison.OrdinalIgnoreCase) ||
            normalizado.Contains("OBLIG", StringComparison.OrdinalIgnoreCase))
        {
            return "SI";
        }

        return "NO";
    }

    private sealed record CacheEntry(CedulaLookupResult Result, DateTimeOffset ExpiresAt);
}

public sealed class CedulaLookupResult
{
    public bool Success { get; set; }
    public bool Found { get; set; }
    public string? Tipo { get; set; }
    public string? Mensaje { get; set; }
    public string? Cedula { get; set; }
    public string? Ruc { get; set; }
    public string? NombreCompleto { get; set; }
    public string? RazonSocial { get; set; }
    public string? NombreComercial { get; set; }
    public string? ObligadoLlevarContabilidad { get; set; }
    public string? TipoContribuyente { get; set; }
    public string? DireccionMatriz { get; set; }
    public string? DireccionEstablecimiento { get; set; }
    public string? NumeroEstablecimiento { get; set; }
    public string? EstadoEstablecimiento { get; set; }
    public string? EstadoCivil { get; set; }
    public string? FechaNacimiento { get; set; }
    public string? LugarNacimiento { get; set; }
    public string? Sexo { get; set; }
    public string? CondicionCiudadano { get; set; }

    public static CedulaLookupResult Fail(string mensaje) => new()
    {
        Success = false,
        Found = false,
        Mensaje = mensaje
    };

    public static CedulaLookupResult NotFound(string mensaje) => new()
    {
        Success = true,
        Found = false,
        Mensaje = mensaje
    };
}
