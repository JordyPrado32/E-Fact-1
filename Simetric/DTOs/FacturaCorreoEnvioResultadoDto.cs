namespace Simetric.DTOs
{
    public class FacturaCorreoEnvioResultadoDto
    {
        public bool Enviado { get; set; }
        public bool PendienteAutorizacion { get; set; }
        public bool SinDestinatarios { get; set; }
        public bool YaEnviado { get; set; }
        public bool Error { get; set; }
        public int TotalDestinatarios { get; set; }
        public string Mensaje { get; set; } = string.Empty;
    }
}
