using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using System.Xml;
using System.Xml.Linq;
using System.Net.Http.Headers;
using Simetric.Models.Glogales;
using System.Globalization;

namespace Simetric.Services;

public sealed class SriXmlProcessorService
{
    private const string DefaultApiKey = "E-factSimetricNumericaKey2026*#";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly FirmaPathResolver _firmaPathResolver;

    public SriXmlProcessorService(
        HttpClient httpClient,
        IConfiguration configuration,
        FirmaPathResolver firmaPathResolver)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _firmaPathResolver = firmaPathResolver;
    }

    public async Task<mensajeSRI> ProcessXmlAsync(
        string rutaXml,
        string? rutaCertificado,
        string? passwordFirma,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
                throw new FileNotFoundException($"No se encontro el archivo XML en la ruta: {rutaXml}");

            EliminarDetallesAdicionalesVacios(rutaXml);

            var rutaCertificadoReal = ResolverRutaCertificado(rutaCertificado);
            if (string.IsNullOrWhiteSpace(rutaCertificadoReal) || !File.Exists(rutaCertificadoReal))
                throw new FileNotFoundException($"No se encontro el archivo de firma (.p12): {rutaCertificado}");

            var payload = new
            {
                RutaXml = rutaXml,
                RutaFirma = rutaCertificadoReal,
                PasswordFirma = passwordFirma ?? string.Empty
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/xml/process")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", ResolveApiKey());
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NumericaSoftwareClient", "1.0"));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var xmlRespuesta = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Respuesta de error de la API: {xmlRespuesta}");

            var serializer = new XmlSerializer(typeof(mensajeSRI));
            using var reader = new StringReader(xmlRespuesta);
            var resultado = serializer.Deserialize(reader) as mensajeSRI
                ?? new mensajeSRI { estado = "ERROR", mensaje = "La respuesta del SRI llego vacia." };

            return NormalizarClaveAccesoRegistrada(resultado, rutaXml);
        }
        catch (OperationCanceledException ex)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "La emision fue cancelada por tiempo de espera.",
                xml = $"<error><tipo>TimeoutSriXml</tipo><mensaje>{System.Security.SecurityElement.Escape(ex.Message)}</mensaje></error>"
            };
        }
        catch (Exception ex)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = ex.Message,
                xml = $"<error><tipo>ExcepcionBlazor</tipo><mensaje>{System.Security.SecurityElement.Escape(ex.Message)}</mensaje></error>"
            };
        }
    }

    private string? ResolverRutaCertificado(string? rutaCertificado)
    {
        return _firmaPathResolver.ResolverRutaExistente(rutaCertificado);
    }

    private string ResolveApiKey()
    {
        return _configuration["SriXmlProcessor:ApiKey"]?.Trim()
            ?? _configuration["SriXml:ApiKey"]?.Trim()
            ?? Environment.GetEnvironmentVariable("SRI_XML_PROCESSOR_API_KEY")?.Trim()
            ?? DefaultApiKey;
    }

    private static mensajeSRI NormalizarClaveAccesoRegistrada(mensajeSRI resultado, string rutaXml)
    {
        var respuestaCompleta = string.Join(
            " ",
            resultado.estado,
            resultado.estadoEnvio,
            resultado.mensaje,
            resultado.xml);

        if (!EsClaveAccesoRegistrada(respuestaCompleta))
            return resultado;

        var claveAcceso = string.IsNullOrWhiteSpace(resultado.autorizacion)
            ? ExtraerClaveAcceso(rutaXml)
            : resultado.autorizacion.Trim();

        if (string.IsNullOrWhiteSpace(claveAcceso))
            return resultado;

        resultado.estado = DocumentoAutorizacionHelper.EstadoAutorizado;
        resultado.estadoEnvio = DocumentoAutorizacionHelper.EstadoAutorizado;
        resultado.autorizacion = claveAcceso;
        resultado.fecha = string.IsNullOrWhiteSpace(resultado.fecha)
            ? DateTime.Now.ToString("O", CultureInfo.InvariantCulture)
            : resultado.fecha;
        resultado.mensaje = "Clave de acceso ya registrada en el SRI; se reconoce la factura como autorizada.";
        resultado.xml = CrearXmlAutorizadoDesdeComprobante(
            rutaXml,
            claveAcceso,
            resultado.fecha);
        return resultado;
    }

    private static bool EsClaveAccesoRegistrada(string? respuesta)
    {
        if (string.IsNullOrWhiteSpace(respuesta))
            return false;

        var texto = QuitarDiacriticos(respuesta).ToUpperInvariant();
        return texto.Contains("CLAVE DE ACCESO YA REGISTRADA", StringComparison.Ordinal)
            || texto.Contains("CLAVE DE ACCESO REGISTRADA", StringComparison.Ordinal)
            || texto.Contains("CLAVE ACCESO YA REGISTRADA", StringComparison.Ordinal)
            || texto.Contains("CLAVE ACCESO REGISTRADA", StringComparison.Ordinal);
    }

    private static string ExtraerClaveAcceso(string rutaXml)
    {
        try
        {
            return XDocument.Load(rutaXml)
                .Descendants()
                .FirstOrDefault(elemento =>
                    string.Equals(elemento.Name.LocalName, "claveAcceso", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim()
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CrearXmlAutorizadoDesdeComprobante(
        string rutaXml,
        string numeroAutorizacion,
        string fechaAutorizacion)
    {
        try
        {
            var comprobante = File.ReadAllText(rutaXml);
            var documento = XDocument.Parse(comprobante);
            var ambienteCodigo = documento
                .Descendants()
                .FirstOrDefault(elemento =>
                    string.Equals(elemento.Name.LocalName, "ambiente", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();

            return new XDocument(
                new XElement(
                    "autorizacion",
                    new XElement("estado", DocumentoAutorizacionHelper.EstadoAutorizado),
                    new XElement("numeroAutorizacion", numeroAutorizacion),
                    new XElement("fechaAutorizacion", fechaAutorizacion),
                    new XElement("ambiente", ambienteCodigo == "1" ? "PRUEBAS" : "PRODUCCION"),
                    new XElement("comprobante", new XCData(comprobante))))
                .ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QuitarDiacriticos(string valor)
    {
        var builder = new StringBuilder(valor.Length);
        foreach (var caracter in valor.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                builder.Append(caracter);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void EliminarDetallesAdicionalesVacios(string rutaXml)
    {
        var documento = XDocument.Load(rutaXml, LoadOptions.PreserveWhitespace);
        var huboCambios = false;

        var camposAdicionales = documento
            .Descendants()
            .Where(elemento =>
                string.Equals(elemento.Name.LocalName, "campoAdicional", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var campo in camposAdicionales)
        {
            var valorNormalizado = NormalizarTextoUnaLinea(campo.Value);
            if (string.IsNullOrWhiteSpace(valorNormalizado))
            {
                campo.Remove();
                huboCambios = true;
            }
            else if (!string.Equals(campo.Value, valorNormalizado, StringComparison.Ordinal))
            {
                campo.Value = valorNormalizado;
                huboCambios = true;
            }
        }

        var detallesAdicionales = documento
            .Descendants()
            .Where(elemento =>
                string.Equals(elemento.Name.LocalName, "detAdicional", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var detalle in detallesAdicionales)
        {
            var atributoValor = detalle.Attribute("valor");
            var valorNormalizado = NormalizarTextoUnaLinea(atributoValor?.Value ?? detalle.Value);
            if (string.IsNullOrWhiteSpace(valorNormalizado))
            {
                detalle.Remove();
                huboCambios = true;
            }
            else if (atributoValor is not null &&
                     !string.Equals(atributoValor.Value, valorNormalizado, StringComparison.Ordinal))
            {
                atributoValor.Value = valorNormalizado;
                huboCambios = true;
            }
        }

        var nombresTextoUnaLinea = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "descripcion",
            "razon",
            "motivo",
            "razonSocial",
            "nombreComercial",
            "razonSocialComprador",
            "razonSocialProveedor",
            "razonSocialTransportista",
            "razonSocialDestinatario",
            "razonSocialSujetoRetenido",
            "direccionComprador",
            "direccionProveedor",
            "dirMatriz",
            "dirEstablecimiento",
            "dirPartida",
            "dirDestinatario"
        };

        foreach (var elemento in documento
                     .Descendants()
                     .Where(elemento => nombresTextoUnaLinea.Contains(elemento.Name.LocalName)))
        {
            var valorNormalizado = NormalizarTextoUnaLinea(elemento.Value);
            if (!string.Equals(elemento.Value, valorNormalizado, StringComparison.Ordinal))
            {
                elemento.Value = valorNormalizado;
                huboCambios = true;
            }
        }

        var contenedoresVacios = documento
            .Descendants()
            .Where(elemento =>
                (string.Equals(elemento.Name.LocalName, "infoAdicional", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(elemento.Name.LocalName, "detallesAdicionales", StringComparison.OrdinalIgnoreCase)) &&
                !elemento.Elements().Any())
            .ToList();

        if (contenedoresVacios.Count > 0)
        {
            contenedoresVacios.Remove();
            huboCambios = true;
        }

        if (!huboCambios)
            return;

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = documento.Declaration is null
        };

        using var writer = XmlWriter.Create(rutaXml, settings);
        documento.Save(writer);
    }

    private static string NormalizarTextoUnaLinea(string? valor) =>
        string.Join(
            " ",
            (valor ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
