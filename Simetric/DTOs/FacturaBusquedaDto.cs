namespace Simetric.DTOs
{
    public class FacturaBusquedaDto
    {
        public int Codfactura { get; set; }
        public string Numfactura { get; set; } = "";
        public string Serie { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
    }
}