using Simetric.Models;

namespace Simetric.DTOs
{
    public class FacturaViewDto
    {
        public Factura Factura { get; set; } = new();
        public Cliente? Cliente { get; set; }
        public Emisor? Emisor { get; set; }
        public string FormaPagoNombre { get; set; } = string.Empty;
        public decimal? DescuentoGlobalPct { get; set; }
        public decimal? DescuentoGlobalValor { get; set; }


        public List<Detallefactura> Detalles { get; set; } = new();
    }
}
