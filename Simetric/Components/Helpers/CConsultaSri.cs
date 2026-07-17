using System.Net;
using System.Text.Json;
using System.Web;
using static ConsultaDeRucApi.Services.CModelo;

namespace ConsultaDeRucApi.Services
{
    public class CConsultaSri
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private CookieContainer Cookie = new();
        private readonly ComprobarConeccion comprobar = new();

        public DatosPersonaSri GetRucSri(string numeroRuc)
        {
            string html = string.Empty;
            var personaDatosError = new DatosPersonaSri();
            var estado = comprobar.ConexionAInternet();

            if (!estado.Equals("Con Coneccion"))
            {
                personaDatosError.Error = "Sin Coneccion";
                return personaDatosError;
            }

            try
            {
                var existeRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/ConsolidadoContribuyente/existePorNumeroRuc?numeroRuc={numeroRuc}");
                html = LeerRespuesta(existeRequest);
            }
            catch (Exception ex)
            {
                personaDatosError.Error = ex.ToString();
                return personaDatosError;
            }

            if (html == "false")
            {
                personaDatosError.Error = "No existe Ruc";
                return personaDatosError;
            }

            Cookie = new CookieContainer();
            var numeroGenerado = Random.Shared.Next(0, 100000000).ToString("D6");

            try
            {
                var captchaStartRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-captcha-servicio-internet/captcha/start/1?r={numeroGenerado}",
                    Cookie);
                html = LeerRespuesta(captchaStartRequest);
            }
            catch (Exception ex)
            {
                personaDatosError.Error = ex.ToString();
                return personaDatosError;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                personaDatosError.Error = "Sin respuesta Catcha";
                return personaDatosError;
            }

            var captchaImage = JsonSerializer.Deserialize<CaptchaImage>(html, JsonOptions);
            var captcha = captchaImage?.values?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(captcha))
            {
                personaDatosError.Error = "No se pudo resolver el captcha del SRI.";
                return personaDatosError;
            }

            string tokenValor;
            try
            {
                var tokenRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-captcha-servicio-internet/rest/ValidacionCaptcha/validarCaptcha/{captcha}?emitirToken=true",
                    Cookie);
                html = LeerRespuesta(tokenRequest);
            }
            catch (Exception ex)
            {
                personaDatosError.Error = ex.ToString();
                return personaDatosError;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                personaDatosError.Error = "No existe contribuyente con ese Ruc";
                return personaDatosError;
            }

            var token = JsonSerializer.Deserialize<Token>(html, JsonOptions);
            tokenValor = token?.mensaje ?? string.Empty;

            if (string.IsNullOrWhiteSpace(tokenValor))
            {
                personaDatosError.Error = "No se pudo obtener el token del SRI.";
                return personaDatosError;
            }

            try
            {
                var contribuyenteRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/ConsolidadoContribuyente/obtenerPorNumerosRuc?&ruc={numeroRuc}",
                    Cookie,
                    tokenValor);
                html = LeerRespuesta(contribuyenteRequest);
            }
            catch (Exception ex)
            {
                personaDatosError.Error = ex.ToString();
                return personaDatosError;
            }

            html = html.Replace("[", "").Replace("]", "");
            var datosPersona = JsonSerializer.Deserialize<DatosPersonaSri>(html, JsonOptions);

            if (datosPersona is null)
            {
                personaDatosError.Error = "No existe contribuyente con ese Ruc";
                return personaDatosError;
            }

            datosPersona.obligado = datosPersona.obligado switch
            {
                null => string.Empty,
                "S" => "Si",
                "N" => "No",
                _ => datosPersona.obligado
            };

            datosPersona.personaSociedad ??= string.Empty;
            if (datosPersona.personaSociedad.Equals("PNL"))
            {
                datosPersona.personaSociedad = "PERSONA NATURAL";
                datosPersona.estadoPersonaNatural = NormalizarEstadoContribuyente(datosPersona.estadoPersonaNatural);
            }

            if (datosPersona.personaSociedad.Equals("SCD"))
            {
                datosPersona.personaSociedad = "SOCIEDAD";
                datosPersona.estadoSociedad = NormalizarEstadoContribuyente(Convert.ToString(datosPersona.estadoSociedad));
            }

            try
            {
                var mipymeRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/ClasificacionMipyme/consultarPorNumeroRuc?numeroRuc={numeroRuc}",
                    Cookie,
                    tokenValor);
                html = LeerRespuesta(mipymeRequest);
                datosPersona.Persona_MiPyme = JsonSerializer.Deserialize<Persona_MiPyme>(html, JsonOptions);
            }
            catch (Exception ex)
            {
                personaDatosError.Error = ex.ToString();
                return personaDatosError;
            }

            try
            {
                var establecimientoRequest = CrearRequest(
                    $"https://srienlinea.sri.gob.ec/sri-catastro-sujeto-servicio-internet/rest/Establecimiento/consultarPorNumeroRuc?numeroRuc={numeroRuc}",
                    Cookie,
                    tokenValor);
                html = LeerRespuesta(establecimientoRequest);
                datosPersona.personaEstablecimientos =
                    JsonSerializer.Deserialize<List<PersonaEstablecimientosRuc>>(html, JsonOptions) ?? new List<PersonaEstablecimientosRuc>();
            }
            catch
            {
                datosPersona.personaEstablecimientos ??= new List<PersonaEstablecimientosRuc>();
            }

            return datosPersona;
        }

        private static HttpWebRequest CrearRequest(string url, CookieContainer? cookieContainer = null, string? authorization = null)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Credentials = CredentialCache.DefaultCredentials;
            request.CookieContainer = cookieContainer ?? new CookieContainer();
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:67.0) Gecko/20100101 Firefox/67.0";
            request.ContentType = "application/json; charset=utf-8";

            if (!string.IsNullOrWhiteSpace(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            return request;
        }

        private static string LeerRespuesta(HttpWebRequest request)
        {
            using var response = (HttpWebResponse)request.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream!);
            return HttpUtility.HtmlDecode(reader.ReadToEnd());
        }

        private static string NormalizarEstadoContribuyente(string? estado) =>
            estado switch
            {
                "ACT" => "ACTIVO",
                "PAS" => "PASIVO",
                "SDE" => "SUSPENDIDO",
                null => string.Empty,
                _ => estado
            };
    }
}
