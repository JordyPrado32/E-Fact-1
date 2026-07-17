using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using System.Net.Http.Headers;
using Simetric.Models.Glogales;

namespace Simetric.Services;

public sealed class SriXmlProcessorService
{
    private const string DefaultApiKey = "E-factSimetricNumericaKey2026*#";
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SriXmlProcessorService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
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
            return serializer.Deserialize(reader) as mensajeSRI
                ?? new mensajeSRI { estado = "ERROR", mensaje = "La respuesta del SRI llego vacia." };
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

    private static string? ResolverRutaCertificado(string? rutaCertificado)
    {
        if (string.IsNullOrWhiteSpace(rutaCertificado))
            return null;

        var candidatos = new[]
        {
            rutaCertificado,
            Path.Combine(Directory.GetCurrentDirectory(), rutaCertificado),
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", Path.GetFileName(rutaCertificado)),
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "certs", "path", Path.GetFileName(rutaCertificado))
        };

        return candidatos.FirstOrDefault(File.Exists) ?? candidatos.Last();
    }

    private string ResolveApiKey()
    {
        return _configuration["SriXmlProcessor:ApiKey"]?.Trim()
            ?? _configuration["SriXml:ApiKey"]?.Trim()
            ?? Environment.GetEnvironmentVariable("SRI_XML_PROCESSOR_API_KEY")?.Trim()
            ?? DefaultApiKey;
    }
}
