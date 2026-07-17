namespace Simetric.DTOs
{
    public class GuiaRemisionGuardadoResultadoDto
    {
        public int SecGuiaRemision { get; set; }
        public int CodigoTransportista { get; set; }
        public int? Codfactura { get; set; }
        public string Serie { get; set; } = string.Empty;
        public string Secuencial { get; set; } = string.Empty;
        public string NumeroCompleto { get; set; } = string.Empty;
        public string RutaXml { get; set; } = string.Empty;
        public string NombreArchivoXml { get; set; } = string.Empty;
        public string RutaPdf { get; set; } = string.Empty;
        public string NombreArchivoPdf { get; set; } = string.Empty;
        public string ClaveAcceso { get; set; } = string.Empty;
    }
}
