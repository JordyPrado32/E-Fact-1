using Microsoft.AspNetCore.Hosting;
using Simetric.Models;

namespace Simetric.Services;

public sealed class EmisorCertificadoValidator
{
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly FirmaInfoApiService _firmaInfoApiService;
    private readonly IConfiguration _configuration;

    public EmisorCertificadoValidator(
        IWebHostEnvironment hostEnvironment,
        EmisorCertificadoProtector certificadoProtector,
        FirmaInfoApiService firmaInfoApiService,
        IConfiguration configuration)
    {
        _hostEnvironment = hostEnvironment;
        _certificadoProtector = certificadoProtector;
        _firmaInfoApiService = firmaInfoApiService;
        _configuration = configuration;
    }

    public CertificadoEmisorValidationResult Validar(Emisor? emisor)
    {
        if (emisor is null)
        {
            return CertificadoEmisorValidationResult.Fail("No se pudo validar la firma electronica del emisor.");
        }

        var rutaRelativa = NormalizarRutaCertificado(emisor.PathCertificado);
        var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado);
        if (string.IsNullOrWhiteSpace(clave) && !string.IsNullOrWhiteSpace(emisor.ClaveCertificado))
            clave = emisor.ClaveCertificado.Trim();

        if (string.IsNullOrWhiteSpace(rutaRelativa) && string.IsNullOrWhiteSpace(clave))
        {
            return CertificadoEmisorValidationResult.NoConfigurado();
        }

        if (string.IsNullOrWhiteSpace(rutaRelativa))
        {
            return CertificadoEmisorValidationResult.Fail("Debes cargar el archivo .p12 de la firma electronica.");
        }

        if (string.IsNullOrWhiteSpace(clave))
        {
            return CertificadoEmisorValidationResult.Fail("Debes ingresar la clave de la firma electronica.");
        }

        return CertificadoEmisorValidationResult.Ok(null, null);
    }

    public async Task<CertificadoEmisorValidationResult> ValidarConApiAsync(
        Emisor? emisor,
        CancellationToken cancellationToken = default)
    {
        var validacionConfiguracion = Validar(emisor);
        if (!validacionConfiguracion.IsValid)
            return validacionConfiguracion;

        var rutaFisica = ResolverRutaParaApi(emisor!.PathCertificado);
        if (string.IsNullOrWhiteSpace(rutaFisica))
            return CertificadoEmisorValidationResult.Fail("No se encontro el archivo .p12 configurado para el emisor.");

        var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado);
        if (string.IsNullOrWhiteSpace(clave))
            return CertificadoEmisorValidationResult.Fail("No se pudo obtener la clave de la firma electronica.");

        var apiResult = await _firmaInfoApiService.ConsultarAsync(rutaFisica, clave, cancellationToken);
        if (!apiResult.Success || apiResult.Info is null)
            return CertificadoEmisorValidationResult.Fail("Firma no válida.");

        var info = apiResult.Info;
        if (!info.EsValida)
            return CertificadoEmisorValidationResult.Fail("Firma no válida.");

        if (!info.TieneClavePrivada)
            return CertificadoEmisorValidationResult.Fail("La firma no contiene una clave privada valida.");

        if (info.FechaExpiracion is null)
            return CertificadoEmisorValidationResult.Fail("El servicio no devolvio la fecha de expiracion de la firma.");

        var identificaciones = new[] { info.Ruc, info.Cedula }
            .Where(valor => !string.IsNullOrWhiteSpace(valor))
            .Select(valor => NormalizarDigitos(valor))
            .Where(valor => !string.IsNullOrWhiteSpace(valor))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var identificacionCoincidente = identificaciones
            .FirstOrDefault(valor => PerteneceAlRuc(valor, emisor.Ruc));

        if (identificacionCoincidente is null)
            return CertificadoEmisorValidationResult.Fail(
                $"La firma pertenece a una identificacion diferente y no coincide con el RUC {emisor.Ruc} del emisor.");

        var estaVigente =
            string.Equals(info.EstadoVigencia?.Trim(), "VIGENTE", StringComparison.OrdinalIgnoreCase) &&
            info.FechaExpiracion.Value > DateTimeOffset.Now;

        if (!estaVigente)
        {
            var fechaExpiracion = info.FechaExpiracion.Value.LocalDateTime;
            return CertificadoEmisorValidationResult.Fail(
                $"La firma electronica esta caducada desde el {info.FechaExpiracion.Value:dd/MM/yyyy}.",
                fechaExpiracion,
                identificacionCoincidente,
                CalcularDiasRestantes(fechaExpiracion),
                info.NombreTitular,
                info.EstadoVigencia);
        }

        var fechaExpiracionVigente = info.FechaExpiracion.Value.LocalDateTime;
        return CertificadoEmisorValidationResult.Ok(
            fechaExpiracionVigente,
            identificacionCoincidente,
            Math.Max(CalcularDiasRestantes(fechaExpiracionVigente), 0),
            info.NombreTitular,
            info.EstadoVigencia);
    }

    private static int CalcularDiasRestantes(DateTime fechaExpiracion) =>
        (fechaExpiracion.Date - DateTime.Today).Days;

    private static bool PerteneceAlRuc(string? identificacionCertificado, string? rucEmisor)
    {
        var identificacion = NormalizarDigitos(identificacionCertificado);
        var ruc = NormalizarDigitos(rucEmisor);

        if (string.IsNullOrWhiteSpace(identificacion) || string.IsNullOrWhiteSpace(ruc))
        {
            return false;
        }

        if (identificacion == ruc)
        {
            return true;
        }

        return identificacion.Length == 10 &&
               ruc.Length == 13 &&
               ruc.StartsWith(identificacion, StringComparison.Ordinal) &&
               ruc.EndsWith("001", StringComparison.Ordinal);
    }

    private static string? NormalizarRutaCertificado(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return null;
        }

        var normalizada = ruta.Trim().TrimStart('~', '/', '\\').Replace('\\', '/');
        if (normalizada.StartsWith("App_Data/", StringComparison.OrdinalIgnoreCase))
        {
            normalizada = normalizada["App_Data/".Length..];
        }

        return normalizada;
    }

    private string? ResolverRutaFisica(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta))
            return null;

        var rutaOriginal = ruta.Trim();
        var rutaNormalizada = NormalizarRutaCertificado(ruta);
        if (string.IsNullOrWhiteSpace(rutaNormalizada))
            return null;

        var candidatos = new List<string>();
        var nombreArchivo = Path.GetFileName(rutaNormalizada);
        var contentRoot = _hostEnvironment.ContentRootPath;
        var webRoot = string.IsNullOrWhiteSpace(_hostEnvironment.WebRootPath)
            ? Path.Combine(contentRoot, "wwwroot")
            : _hostEnvironment.WebRootPath;

        if (Path.IsPathRooted(rutaOriginal))
            candidatos.Add(rutaOriginal);

        void AgregarCandidato(string baseDir, string relativePath)
        {
            if (!string.IsNullOrWhiteSpace(baseDir) && !string.IsNullOrWhiteSpace(relativePath))
                candidatos.Add(Path.Combine(
                    baseDir,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        AgregarCandidato(contentRoot, $"App_Data/{rutaNormalizada}");
        AgregarCandidato(webRoot, $"App_Data/{rutaNormalizada}");
        AgregarCandidato(contentRoot, rutaNormalizada);
        AgregarCandidato(webRoot, rutaNormalizada);
        AgregarCandidato(contentRoot, $"App_Data/certs/path/{nombreArchivo}");
        AgregarCandidato(webRoot, $"App_Data/certs/path/{nombreArchivo}");
        AgregarCandidato(contentRoot, $"App_Data/certs/system/{nombreArchivo}");
        AgregarCandidato(webRoot, $"App_Data/certs/system/{nombreArchivo}");

        return candidatos
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private string? ResolverRutaParaApi(string? ruta)
    {
        var rutaLocal = ResolverRutaFisica(ruta);
        if (!string.IsNullOrWhiteSpace(rutaLocal))
            return rutaLocal;

        var rutaFirmasBase = _configuration["FirmaInfoApi:RutaFirmasBase"]?.Trim();
        var rutaNormalizada = NormalizarRutaCertificado(ruta);
        if (string.IsNullOrWhiteSpace(rutaFirmasBase) || string.IsNullOrWhiteSpace(rutaNormalizada))
            return null;

        var rutaRelativa = rutaNormalizada.Replace('/', Path.DirectorySeparatorChar);
        if (!rutaNormalizada.Contains('/'))
            rutaRelativa = Path.Combine("certs", "path", rutaRelativa);

        return Path.GetFullPath(Path.Combine(rutaFirmasBase, rutaRelativa));
    }

    private static string? NormalizarDigitos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digitos) ? null : digitos;
    }
}

public sealed record CertificadoEmisorValidationResult(
    bool IsValid,
    bool TieneConfiguracion,
    string Message,
    DateTime? FechaExpiracion = null,
    string? IdentificacionExtraida = null,
    int? DiasRestantes = null,
    string? NombreTitular = null,
    string? EstadoVigencia = null)
{
    public static CertificadoEmisorValidationResult Ok(
        DateTime? fechaExpiracion,
        string? identificacionExtraida,
        int? diasRestantes = null,
        string? nombreTitular = null,
        string? estadoVigencia = null) =>
        new(
            true,
            true,
            string.Empty,
            fechaExpiracion,
            identificacionExtraida,
            diasRestantes,
            nombreTitular,
            estadoVigencia);

    public static CertificadoEmisorValidationResult NoConfigurado() =>
        new(false, false, EmisionControlService.MensajeFirmaRequerida);

    public static CertificadoEmisorValidationResult Fail(
        string message,
        DateTime? fechaExpiracion = null,
        string? identificacionExtraida = null,
        int? diasRestantes = null,
        string? nombreTitular = null,
        string? estadoVigencia = null) =>
        new(
            false,
            true,
            message,
            fechaExpiracion,
            identificacionExtraida,
            diasRestantes,
            nombreTitular,
            estadoVigencia);
}
