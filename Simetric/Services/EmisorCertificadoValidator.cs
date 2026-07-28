using Simetric.Models;

namespace Simetric.Services;

public sealed class EmisorCertificadoValidator
{
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly FirmaInfoApiService _firmaInfoApiService;
    private readonly FirmaPathResolver _firmaPathResolver;

    public EmisorCertificadoValidator(
        EmisorCertificadoProtector certificadoProtector,
        FirmaInfoApiService firmaInfoApiService,
        FirmaPathResolver firmaPathResolver)
    {
        _certificadoProtector = certificadoProtector;
        _firmaInfoApiService = firmaInfoApiService;
        _firmaPathResolver = firmaPathResolver;
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

        var rutasFirma = _firmaPathResolver.ResolverRutasParaApi(emisor!.PathCertificado);
        if (rutasFirma.Count == 0)
            return CertificadoEmisorValidationResult.Fail("No se encontro el archivo .p12 configurado para el emisor.");

        var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado);
        if (string.IsNullOrWhiteSpace(clave))
            return CertificadoEmisorValidationResult.Fail("No se pudo obtener la clave de la firma electronica.");

        FirmaInfoApiResult? apiResult = null;
        foreach (var rutaFirma in rutasFirma)
        {
            apiResult = await _firmaInfoApiService.ConsultarAsync(rutaFirma, clave, cancellationToken);
            if (apiResult.Success || cancellationToken.IsCancellationRequested)
                break;
        }

        apiResult ??= FirmaInfoApiResult.Error("No se pudo validar la ruta del archivo de firma.");
        if (!apiResult.Success || apiResult.Info is null)
            return CertificadoEmisorValidationResult.Fail(
                string.IsNullOrWhiteSpace(apiResult.Message) ? "Firma no valida." : apiResult.Message,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

        var info = apiResult.Info;
        if (!info.EsValida)
            return CertificadoEmisorValidationResult.Fail(
                string.IsNullOrWhiteSpace(info.Mensaje) ? "Firma no valida." : info.Mensaje,
                nombreTitular: info.NombreTitular,
                estadoVigencia: info.EstadoVigencia,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

        if (!info.TieneClavePrivada)
            return CertificadoEmisorValidationResult.Fail(
                "La firma no contiene una clave privada valida.",
                nombreTitular: info.NombreTitular,
                estadoVigencia: info.EstadoVigencia,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

        if (info.FechaExpiracion is null)
            return CertificadoEmisorValidationResult.Fail(
                "El servicio no devolvio la fecha de expiracion de la firma.",
                identificacionExtraida: FirstFilled(info.Ruc, info.Cedula),
                nombreTitular: info.NombreTitular,
                estadoVigencia: info.EstadoVigencia,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

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
                $"La firma pertenece a una identificacion diferente y no coincide con el RUC {emisor.Ruc} del emisor.",
                identificacionExtraida: FirstFilled(info.Ruc, info.Cedula),
                nombreTitular: info.NombreTitular,
                estadoVigencia: info.EstadoVigencia,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

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
                info.EstadoVigencia,
                apiResult.RawJson,
                apiResult.HttpStatusCode,
                apiResult.Success);
        }

        var fechaExpiracionVigente = info.FechaExpiracion.Value.LocalDateTime;
        return CertificadoEmisorValidationResult.Ok(
            fechaExpiracionVigente,
            identificacionCoincidente,
            Math.Max(CalcularDiasRestantes(fechaExpiracionVigente), 0),
            info.NombreTitular,
            info.EstadoVigencia,
            apiResult.RawJson,
            apiResult.HttpStatusCode,
            apiResult.Success);
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

    private static string? NormalizarDigitos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digitos) ? null : digitos;
    }

    private static string? FirstFilled(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

public sealed record CertificadoEmisorValidationResult(
    bool IsValid,
    bool TieneConfiguracion,
    string Message,
    DateTime? FechaExpiracion = null,
    string? IdentificacionExtraida = null,
    int? DiasRestantes = null,
    string? NombreTitular = null,
    string? EstadoVigencia = null,
    string? ApiResponseJson = null,
    int? ApiHttpStatusCode = null,
    bool? ApiSuccess = null)
{
    public static CertificadoEmisorValidationResult Ok(
        DateTime? fechaExpiracion,
        string? identificacionExtraida,
        int? diasRestantes = null,
        string? nombreTitular = null,
        string? estadoVigencia = null,
        string? apiResponseJson = null,
        int? apiHttpStatusCode = null,
        bool? apiSuccess = null) =>
        new(
            true,
            true,
            string.Empty,
            fechaExpiracion,
            identificacionExtraida,
            diasRestantes,
            nombreTitular,
            estadoVigencia,
            apiResponseJson,
            apiHttpStatusCode,
            apiSuccess);

    public static CertificadoEmisorValidationResult NoConfigurado() =>
        new(false, false, EmisionControlService.MensajeFirmaRequerida);

    public static CertificadoEmisorValidationResult Fail(
        string message,
        DateTime? fechaExpiracion = null,
        string? identificacionExtraida = null,
        int? diasRestantes = null,
        string? nombreTitular = null,
        string? estadoVigencia = null,
        string? apiResponseJson = null,
        int? apiHttpStatusCode = null,
        bool? apiSuccess = null) =>
        new(
            false,
            true,
            message,
            fechaExpiracion,
            identificacionExtraida,
            diasRestantes,
            nombreTitular,
            estadoVigencia,
            apiResponseJson,
            apiHttpStatusCode,
            apiSuccess);
}
