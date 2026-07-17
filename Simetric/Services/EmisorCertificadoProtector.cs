using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;

namespace Simetric.Services;

public class EmisorCertificadoProtector
{
    private const int MaxInlineLength = 50;
    private const string VaultPrefix = "vault:";
    private readonly IDataProtector _protector;
    private readonly string _vaultDirectory;

    public EmisorCertificadoProtector(
        IDataProtectionProvider dataProtectionProvider,
        IWebHostEnvironment hostEnvironment)
    {
        _protector = dataProtectionProvider.CreateProtector("Simetric.Emisor.Certificado.Clave.v1");
        _vaultDirectory = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "certs", "keys");
    }

    public string? ProtegerClave(string? valorPlano)
    {
        var normalizado = NormalizarClave(valorPlano);
        if (string.IsNullOrWhiteSpace(normalizado))
        {
            return null;
        }

        var protegido = _protector.Protect(normalizado);
        if (protegido.Length <= MaxInlineLength)
            return protegido;

        try
        {
            return GuardarEnVault(protegido);
        }
        catch
        {
            // En publicado puede no existir permiso de escritura para el vault.
            // Si falla, conservamos la clave protegida en linea para no romper el alta.
            return protegido;
        }
    }

    public string? DesprotegerClave(string? valorProtegido)
    {
        if (string.IsNullOrWhiteSpace(valorProtegido))
        {
            return null;
        }

        if (EsReferenciaVault(valorProtegido))
        {
            var contenidoVault = LeerDesdeVault(valorProtegido);
            if (string.IsNullOrWhiteSpace(contenidoVault))
            {
                return null;
            }

            valorProtegido = contenidoVault;
        }

        try
        {
            return _protector.Unprotect(valorProtegido);
        }
        catch
        {
            return valorProtegido.Trim();
        }
    }

    public void EliminarClavePersistida(string? valorPersistido)
    {
        var filePath = ObtenerRutaVault(valorPersistido);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Si el archivo ya no existe o está bloqueado, evitamos romper el flujo principal.
        }
    }

    private string GuardarEnVault(string valorProtegido)
    {
        Directory.CreateDirectory(_vaultDirectory);

        var token = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_vaultDirectory, $"{token}.txt");
        File.WriteAllText(filePath, valorProtegido);

        return $"{VaultPrefix}{token}";
    }

    private string? LeerDesdeVault(string referencia)
    {
        var filePath = ObtenerRutaVault(referencia);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        return File.ReadAllText(filePath).Trim();
    }

    private string? ObtenerRutaVault(string? referencia)
    {
        if (!EsReferenciaVault(referencia))
        {
            return null;
        }

        var token = referencia![VaultPrefix.Length..].Trim();
        if (!EsTokenHexadecimal(token))
        {
            return null;
        }

        return Path.Combine(_vaultDirectory, $"{token}.txt");
    }

    private static bool EsReferenciaVault(string? referencia) =>
        !string.IsNullOrWhiteSpace(referencia) &&
        referencia.StartsWith(VaultPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool EsTokenHexadecimal(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public static string? NormalizarClave(string? valor) => valor?.Trim();
}
