using Microsoft.AspNetCore.Hosting;
using Simetric.Models;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Simetric.Services;

public sealed class EmisorCertificadoValidator
{
    private static readonly Regex DigitosRegex = new(@"\d{10,13}", RegexOptions.Compiled);
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly EmisorCertificadoProtector _certificadoProtector;

    public EmisorCertificadoValidator(
        IWebHostEnvironment hostEnvironment,
        EmisorCertificadoProtector certificadoProtector)
    {
        _hostEnvironment = hostEnvironment;
        _certificadoProtector = certificadoProtector;
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

    private static string? ExtraerIdentificacion(X509Certificate2 certificado, string? rucEmisor)
    {
        return SeleccionarIdentificacion(ExtraerIdentificaciones(certificado), rucEmisor);
    }

    private static List<string> ExtraerIdentificaciones(X509Certificate2 certificado)
    {
        var candidatos = new List<string>();
        AgregarCoincidencias(candidatos, certificado.Subject);
        AgregarCoincidencias(candidatos, certificado.Issuer);
        AgregarCoincidencias(candidatos, certificado.SubjectName?.Name);
        AgregarCoincidencias(candidatos, certificado.IssuerName?.Name);
        AgregarCoincidencias(candidatos, certificado.SerialNumber);

        foreach (var extension in certificado.Extensions)
        {
            try
            {
                AgregarCoincidencias(candidatos, extension.Format(multiLine: true));
                AgregarCoincidencias(candidatos, Convert.ToHexString(extension.RawData));
            }
            catch
            {
            }
        }

        return candidatos;
    }

    private static string? SeleccionarIdentificacion(List<string> candidatos, string? rucEmisor)
    {
        var rucNormalizado = NormalizarDigitos(rucEmisor);
        if (string.IsNullOrWhiteSpace(rucNormalizado))
        {
            return candidatos.FirstOrDefault();
        }

        return candidatos.FirstOrDefault(c => PerteneceAlRuc(c, rucNormalizado))
            ?? candidatos.FirstOrDefault();
    }

    private static void AgregarCoincidencias(List<string> candidatos, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return;
        }

        foreach (Match match in DigitosRegex.Matches(texto))
        {
            var valor = NormalizarDigitos(match.Value);
            if (string.IsNullOrWhiteSpace(valor))
            {
                continue;
            }

            if (valor.Length is 10 or 13 && !candidatos.Contains(valor, StringComparer.Ordinal))
            {
                candidatos.Add(valor);
            }
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
}

public sealed record CertificadoEmisorValidationResult(
    bool IsValid,
    bool TieneConfiguracion,
    string Message,
    DateTime? FechaExpiracion = null,
    string? IdentificacionExtraida = null)
{
    public static CertificadoEmisorValidationResult Ok(DateTime? fechaExpiracion, string? identificacionExtraida) =>
        new(true, true, string.Empty, fechaExpiracion, identificacionExtraida);

    public static CertificadoEmisorValidationResult NoConfigurado() =>
        new(false, false, EmisionControlService.MensajeFirmaRequerida);

    public static CertificadoEmisorValidationResult Fail(string message) =>
        new(false, true, message);
}
