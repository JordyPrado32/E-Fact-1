using Simetric.Models;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Simetric.Services;

public sealed class EmisorCertificadoValidator
{
    private static readonly Regex IdentificacionRegex = new(@"\d{10,13}", RegexOptions.Compiled);
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

        if (apiResult is { Success: false } && !cancellationToken.IsCancellationRequested)
        {
            var rutaLocal = _firmaPathResolver.ResolverRutaExistente(emisor.PathCertificado);
            if (!string.IsNullOrWhiteSpace(rutaLocal))
                apiResult = ValidarArchivoLocal(rutaLocal, clave);
        }

        apiResult ??= FirmaInfoApiResult.Error("No se pudo validar la ruta del archivo de firma.");
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
            apiResult.Success);
    }

    private static int CalcularDiasRestantes(DateTime fechaExpiracion) =>
        (fechaExpiracion.Date - DateTime.Today).Days;

    private static FirmaInfoApiResult ValidarArchivoLocal(string rutaFirma, string passwordFirma)
    {
        var archivo = new FileInfo(rutaFirma);
        if (!archivo.Exists)
            return FirmaInfoApiResult.Error("No se encontró el archivo .p12 configurado.");

        if (archivo.Length == 0)
            return FirmaInfoApiResult.Error("El archivo .p12 está vacío. Carga nuevamente la firma electrónica.");

        try
        {
            // 1. Usa la bandera MachineKeySet para que el usuario IWPD_ no dependa de un perfil cargado.
            using var certificado = new X509Certificate2(
                rutaFirma,
                passwordFirma,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

            var ahora = DateTime.Now;
            var estaVigente = certificado.NotBefore <= ahora && certificado.NotAfter > ahora;
            var aunNoVigente = certificado.NotBefore > ahora;
            var identificaciones = ExtraerIdentificaciones(certificado);

            var info = new FirmaInfoApiResponse
            {
                EsValida = estaVigente && certificado.HasPrivateKey,
                EstadoVigencia = estaVigente ? "VIGENTE" : aunNoVigente ? "NO_VIGENTE" : "CADUCADA",
                Mensaje = estaVigente
                    ? "Firma valida."
                    : aunNoVigente
                        ? $"La firma electronica sera valida desde el {certificado.NotBefore:dd/MM/yyyy}."
                        : $"La firma electronica esta caducada desde el {certificado.NotAfter:dd/MM/yyyy}.",
                NombreTitular = certificado.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                Ruc = identificaciones.FirstOrDefault(valor => valor.Length == 13),
                Cedula = identificaciones.FirstOrDefault(valor => valor.Length == 10),
                FechaEmision = certificado.NotBefore,
                FechaExpiracion = certificado.NotAfter,
                DiasRestantes = CalcularDiasRestantes(certificado.NotAfter),
                TieneClavePrivada = certificado.HasPrivateKey
            };

            return FirmaInfoApiResult.Ok(info);
        }
        catch (CryptographicException ex)
        {
            return FirmaInfoApiResult.Error(ObtenerMensajeErrorCertificado(ex));
        }
    }

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

    private static string ObtenerMensajeErrorCertificado(CryptographicException exception)
    {
        var detalle = exception.Message;
        if (detalle.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            detalle.Contains("contraseña", StringComparison.OrdinalIgnoreCase))
        {
            return "La clave ingresada no corresponde al archivo .p12. Verifica la clave e intenta nuevamente.";
        }

        if (detalle.Contains("ASN1", StringComparison.OrdinalIgnoreCase) ||
            detalle.Contains("bad data", StringComparison.OrdinalIgnoreCase) ||
            detalle.Contains("datos no válidos", StringComparison.OrdinalIgnoreCase) ||
            detalle.Contains("decode", StringComparison.OrdinalIgnoreCase))
        {
            return "El archivo no tiene un formato .p12 válido o está dañado. Carga nuevamente el archivo original.";
        }

        return "No se pudo abrir la firma electrónica. La clave no corresponde al archivo o el archivo .p12 está dañado.";
    }

    private static IReadOnlyList<string> ExtraerIdentificaciones(X509Certificate2 certificado)
    {
        var identificaciones = new List<string>();
        AgregarIdentificaciones(identificaciones, certificado.Subject);
        AgregarIdentificaciones(identificaciones, certificado.SubjectName.Name);

        foreach (var extension in certificado.Extensions)
        {
            try
            {
                AgregarIdentificaciones(identificaciones, extension.Format(multiLine: true));
            }
            catch (CryptographicException)
            {
            }
        }

        return identificaciones;
    }

    private static void AgregarIdentificaciones(ICollection<string> identificaciones, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return;

        foreach (Match coincidencia in IdentificacionRegex.Matches(texto))
        {
            if (!identificaciones.Contains(coincidencia.Value, StringComparer.Ordinal))
                identificaciones.Add(coincidencia.Value);
        }
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
