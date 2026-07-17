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

            System.Net.WebRequest WebRequest;
            WebRequest = System.Net.WebRequest.Create(Url);
            System.Net.WebResponse objetoResp;

            try
            {
                objetoResp = WebRequest.GetResponse();
                Estado = "Con Coneccion";
                objetoResp.Close();

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

