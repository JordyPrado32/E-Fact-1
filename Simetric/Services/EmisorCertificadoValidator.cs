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
            clave = emisor.ClaveCertificado.StartsWith("CfDJ", StringComparison.Ordinal)
                ? null
                : emisor.ClaveCertificado;

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

        var rutaLocal = _firmaPathResolver.ResolverRutaExistente(emisor!.PathCertificado);
        if (string.IsNullOrWhiteSpace(rutaLocal))
            return CertificadoEmisorValidationResult.Fail("No se encontro el archivo .p12 configurado para el emisor.");

        var clave = _certificadoProtector.DesprotegerClave(emisor.ClaveCertificado);
        if (string.IsNullOrWhiteSpace(clave))
            return CertificadoEmisorValidationResult.Fail("No se pudo obtener la clave de la firma electronica.");

        var apiResult = await ConsultarArchivoConApiAsync(emisor.PathCertificado!, clave, cancellationToken);

        if (!apiResult.Success || apiResult.Info is null)
            return CertificadoEmisorValidationResult.Fail(
                string.IsNullOrWhiteSpace(apiResult.Message) ? "Firma no valida." : apiResult.Message,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

        var info = apiResult.Info;
        if (!info.TieneClavePrivada)
            return CertificadoEmisorValidationResult.Fail(
                "El archivo no contiene la clave privada requerida para firmar. Verifica que hayas cargado el archivo .p12 correcto.",
                nombreTitular: info.NombreTitular,
                estadoVigencia: info.EstadoVigencia,
                apiResponseJson: apiResult.RawJson,
                apiHttpStatusCode: apiResult.HttpStatusCode,
                apiSuccess: apiResult.Success);

        if (!info.EsValida)
            return CertificadoEmisorValidationResult.Fail(
                ConstruirMensajeFirmaInvalida(info),
                fechaExpiracion: info.FechaExpiracion?.LocalDateTime,
                identificacionExtraida: FirstFilled(info.Ruc, info.Cedula),
                diasRestantes: info.FechaExpiracion is null
                    ? null
                    : CalcularDiasRestantes(info.FechaExpiracion.Value.LocalDateTime),
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
            apiResult.Success,
            info.FechaEmision?.LocalDateTime,
            info.EmisorCertificado,
            info.NumeroSerie,
            info.HuellaDigital);
    }

    public async Task<FirmaInfoApiResult> ConsultarArchivoConApiAsync(
        string rutaFirma,
        string clave,
        CancellationToken cancellationToken = default)
    {
        var rutaParaApi = _firmaPathResolver.ResolverRutaParaApi(rutaFirma);
        if (string.IsNullOrWhiteSpace(rutaParaApi))
            return FirmaInfoApiResult.Error("No se encontro el archivo .p12 para validar.");

        return await _firmaInfoApiService.ConsultarAsync(rutaParaApi, clave, cancellationToken);
    }

    private static int CalcularDiasRestantes(DateTime fechaExpiracion) =>
        (fechaExpiracion.Date - DateTime.Today).Days;

    private static string ConstruirMensajeFirmaInvalida(FirmaInfoApiResponse info)
    {
        var estado = info.EstadoVigencia?.Trim();
        if (string.Equals(estado, "CADUCADA", StringComparison.OrdinalIgnoreCase) &&
            info.FechaExpiracion is not null)
        {
            return $"La firma electrónica caducó el {info.FechaExpiracion.Value:dd/MM/yyyy}. Debes cargar una firma vigente.";
        }

        if ((string.Equals(estado, "NO_VIGENTE", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(estado, "AUN_NO_VIGENTE", StringComparison.OrdinalIgnoreCase)) &&
            info.FechaEmision is not null)
        {
            return $"La firma electrónica todavía no está vigente. Será válida desde el {info.FechaEmision.Value:dd/MM/yyyy}.";
        }

        if (!EsMensajeGenerico(info.Mensaje))
            return info.Mensaje!.Trim();

        return "No se pudo validar la firma. Verifica que la clave corresponda al archivo .p12 y que el archivo no esté dañado.";
    }

    private static bool EsMensajeGenerico(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return true;

        var normalizado = mensaje.Trim().TrimEnd('.');
        return string.Equals(normalizado, "Firma no valida", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizado, "Firma inválida", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizado, "Firma invalida", StringComparison.OrdinalIgnoreCase);
    }

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
    bool? ApiSuccess = null,
    DateTime? FechaEmision = null,
    string? EmisorCertificado = null,
    string? NumeroSerie = null,
    string? HuellaDigital = null)
{
    public static CertificadoEmisorValidationResult Ok(
        DateTime? fechaExpiracion,
        string? identificacionExtraida,
        int? diasRestantes = null,
        string? nombreTitular = null,
        string? estadoVigencia = null,
        string? apiResponseJson = null,
        int? apiHttpStatusCode = null,
        bool? apiSuccess = null,
        DateTime? fechaEmision = null,
        string? emisorCertificado = null,
        string? numeroSerie = null,
        string? huellaDigital = null) =>
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
            apiSuccess,
            fechaEmision,
            emisorCertificado,
            numeroSerie,
            huellaDigital);

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
