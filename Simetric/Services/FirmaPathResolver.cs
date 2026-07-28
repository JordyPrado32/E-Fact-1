using Microsoft.AspNetCore.Hosting;

namespace Simetric.Services;

public sealed class FirmaPathResolver
{
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;

    public FirmaPathResolver(
        IWebHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
    }

    public string? ResolverRutaExistente(string? rutaFirma)
    {
        return CrearCandidatos(rutaFirma).FirstOrDefault(File.Exists);
    }

    public string? ResolverRutaParaApi(string? rutaFirma)
    {
        return ResolverRutasParaApi(rutaFirma).FirstOrDefault();
    }

    public IReadOnlyList<string> ResolverRutasParaApi(string? rutaFirma)
    {
        var candidatos = new List<string>();
        var rutaPublicadaBase = _configuration["FirmaInfoApi:RutaFirmasBase"]?.Trim();
        var rutaRelativa = NormalizarRutaRelativa(rutaFirma);
        if (!string.IsNullOrWhiteSpace(rutaPublicadaBase) && !string.IsNullOrWhiteSpace(rutaRelativa))
        {
            if (!rutaRelativa.Contains('/'))
                rutaRelativa = $"certs/path/{rutaRelativa}";

            candidatos.Add(Path.GetFullPath(Path.Combine(
                rutaPublicadaBase,
                rutaRelativa.Replace('/', Path.DirectorySeparatorChar))));

            var nombreArchivo = Path.GetFileName(rutaRelativa);
            candidatos.Add(Path.GetFullPath(Path.Combine(
                rutaPublicadaBase,
                "certs",
                "path",
                nombreArchivo)));
        }

        var rutaLocal = ResolverRutaExistente(rutaFirma);
        if (!string.IsNullOrWhiteSpace(rutaLocal))
            candidatos.Add(rutaLocal);

        return candidatos
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? NormalizarRutaRelativa(string? rutaFirma)
    {
        if (string.IsNullOrWhiteSpace(rutaFirma))
            return null;

        var ruta = rutaFirma.Trim().Replace('\\', '/');
        var posicionAppData = ruta.IndexOf("App_Data/", StringComparison.OrdinalIgnoreCase);
        if (posicionAppData >= 0)
            ruta = ruta[(posicionAppData + "App_Data/".Length)..];
        else if (Path.IsPathRooted(rutaFirma.Trim()))
            ruta = Path.GetFileName(ruta);
        else
            ruta = ruta.TrimStart('~', '/');

        return string.IsNullOrWhiteSpace(ruta) ? null : ruta;
    }

    private IEnumerable<string> CrearCandidatos(string? rutaFirma)
    {
        if (string.IsNullOrWhiteSpace(rutaFirma))
            return [];

        var rutaOriginal = rutaFirma.Trim();
        var rutaRelativa = NormalizarRutaRelativa(rutaFirma);
        if (string.IsNullOrWhiteSpace(rutaRelativa))
            return [];

        var contentRoot = _hostEnvironment.ContentRootPath;
        var webRoot = string.IsNullOrWhiteSpace(_hostEnvironment.WebRootPath)
            ? Path.Combine(contentRoot, "wwwroot")
            : _hostEnvironment.WebRootPath;
        var rutaPublicadaBase = _configuration["FirmaInfoApi:RutaFirmasBase"]?.Trim();
        var nombreArchivo = Path.GetFileName(rutaRelativa);
        var candidatos = new List<string>();

        if (Path.IsPathRooted(rutaOriginal))
            candidatos.Add(rutaOriginal);

        Agregar(candidatos, contentRoot, $"App_Data/{rutaRelativa}");
        Agregar(candidatos, webRoot, $"App_Data/{rutaRelativa}");
        Agregar(candidatos, contentRoot, rutaRelativa);
        Agregar(candidatos, webRoot, rutaRelativa);
        Agregar(candidatos, rutaPublicadaBase, rutaRelativa);

        foreach (var subcarpeta in new[] { "certs/path", "certs/system" })
        {
            var rutaPorNombre = $"{subcarpeta}/{nombreArchivo}";
            Agregar(candidatos, contentRoot, $"App_Data/{rutaPorNombre}");
            Agregar(candidatos, webRoot, $"App_Data/{rutaPorNombre}");
            Agregar(candidatos, rutaPublicadaBase, rutaPorNombre);
        }

        return candidatos
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void Agregar(ICollection<string> candidatos, string? basePath, string rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return;

        candidatos.Add(Path.Combine(
            basePath,
            rutaRelativa.Replace('/', Path.DirectorySeparatorChar)));
    }
}
