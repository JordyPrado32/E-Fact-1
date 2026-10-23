using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ConsultaDeRucApi.Services
{
    public class ComprobarConeccion
    {

        public string ConexionAInternet()
        {
            string Estado = "";
            Uri Url = new Uri("https://www.google.com/");

            try
            {
                using var client = new System.Net.Http.HttpClient();
                using var response = client.Send(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, Url));
                Estado = response.IsSuccessStatusCode ? "Con Coneccion" : "No se pudo conectar a Internet";
                return Estado;
            }
            catch (Exception e)
            {
                Estado = "No se pudo conectar a Internet " + e.Message;
                return Estado;
            }

        }
    }
    public class RUCSriDTo
    {
        [StringLength(maximumLength: 13, MinimumLength = 13)]
        [RegularExpression("(^[0-9]+$)", ErrorMessage = "Solo se permiten números")]
        [Required(ErrorMessage = "Campo Requerido")]
        public string clienteCiruc { get; set; } = string.Empty;

    }
    public class CModelo
    {
        public class Token
        {
            public string mensaje { get; set; } = string.Empty;
        }
        public class CaptchaImage
        {
            public string imageName { get; set; } = string.Empty;
            public string imageFieldName { get; set; } = string.Empty;
            public List<string> values { get; set; } = new();
            public string audioFieldName { get; set; } = string.Empty;
        }
    }
}

