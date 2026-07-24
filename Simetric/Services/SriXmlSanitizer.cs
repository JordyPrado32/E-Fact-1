using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Simetric.Services;

internal static partial class SriXmlSanitizer
{
    private static readonly HashSet<string> ElementosOpcionales = new(StringComparer.Ordinal)
    {
        "agenteRetencion",
        "campoAdicional",
        "codEstabDestino",
        "codigoAdicional",
        "codigoAuxiliar",
        "contribuyenteEspecial",
        "detAdicional",
        "detallesAdicionales",
        "direccionComprador",
        "direccionProveedor",
        "dirEstablecimiento",
        "docAduaneroUnico",
        "fechaEmisionDocSustento",
        "guiaRemision",
        "infoAdicional",
        "nombreComercial",
        "numAutDocSustento",
        "numDocSustento",
        "plazo",
        "ruta",
        "unidadTiempo"
    };

    public static void Preparar(XContainer contenedor)
    {
        var elementos = ObtenerElementos(contenedor).ToList();

        foreach (var atributo in elementos.SelectMany(elemento => elemento.Attributes()))
            atributo.Value = Normalizar(atributo.Value);

        foreach (var texto in elementos.SelectMany(elemento => elemento.Nodes().OfType<XText>()))
            texto.Value = Normalizar(texto.Value);

        foreach (var elemento in elementos
                     .Where(elemento => ElementosOpcionales.Contains(elemento.Name.LocalName))
                     .OrderByDescending(ObtenerProfundidad))
        {
            if (EsOpcionalVacio(elemento))
                elemento.Remove();
        }

        var camposVacios = ObtenerElementos(contenedor)
            .Where(EsElementoRequeridoVacio)
            .Select(elemento => elemento.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (camposVacios.Count > 0)
        {
            throw new InvalidOperationException(
                $"No se puede generar el XML porque existen campos requeridos vacíos: {string.Join(", ", camposVacios)}.");
        }
    }

    private static IEnumerable<XElement> ObtenerElementos(XContainer contenedor) =>
        contenedor switch
        {
            XDocument documento => documento.Descendants(),
            XElement elemento => elemento.DescendantsAndSelf(),
            _ => contenedor.Descendants()
        };

    private static bool EsOpcionalVacio(XElement elemento)
    {
        if (!ElementosOpcionales.Contains(elemento.Name.LocalName))
            return false;

        if (elemento.Name.LocalName == "detAdicional")
            return string.IsNullOrWhiteSpace(elemento.Attribute("valor")?.Value);

        return !elemento.HasElements && string.IsNullOrWhiteSpace(elemento.Value);
    }

    private static bool EsElementoRequeridoVacio(XElement elemento)
    {
        if (ElementosOpcionales.Contains(elemento.Name.LocalName) || elemento.HasElements)
            return false;

        return string.IsNullOrWhiteSpace(elemento.Value) &&
               !elemento.Attributes().Any(atributo => !string.IsNullOrWhiteSpace(atributo.Value));
    }

    private static int ObtenerProfundidad(XElement elemento) =>
        elemento.Ancestors().Count();

    private static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        var limpio = new StringBuilder(valor.Length);
        foreach (var caracter in valor)
        {
            if (XmlConvert.IsXmlChar(caracter))
                limpio.Append(char.IsWhiteSpace(caracter) ? ' ' : caracter);
        }

        return EspaciosRepetidosRegex().Replace(limpio.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex EspaciosRepetidosRegex();
}
