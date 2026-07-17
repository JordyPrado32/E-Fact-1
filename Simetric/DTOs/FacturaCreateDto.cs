using Simetric.Models;

namespace Simetric.DTOs
{
    public class FacturaCreateDto
    {
        public Factura Factura { get; set; } = new();
        public Cliente Cliente { get; set; } = new();
        public decimal? DescuentoGlobalPct { get; set; }   // recomendado: 0.05 = 5%
        public decimal? DescuentoGlobalValor { get; set; }
        public List<DetalleFacturaDto> Detalles { get; set; } = new();
    }
}
