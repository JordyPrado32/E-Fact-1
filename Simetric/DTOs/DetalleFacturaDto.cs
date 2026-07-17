namespace Simetric.DTOs
{
    public class DetalleFacturaDto
    {
        public int Codproducto { get; set; }
        public string? Codprincipal { get; set; }
        public string? Codauxiliar { get; set; }

        public decimal Cantidad { get; set; }
        public string? Descripcion { get; set; }
        public string? Detalle { get; set; }
        public decimal PorcentajeDescuento { get; set; }
        public decimal Precio { get; set; }
        public decimal Descuento { get; set; }

        // IVA normal (0, 12, 15, etc.)
        public int Tarifa { get; set; }

        // ✅ (opcional) Para permitir "Personalizado"
        public bool TarifaPersonalizada { get; set; } = false;
        public int? TarifaManual { get; set; }

        // extras que pediste
        public int TipoProducto { get; set; }
        public int? SubtipoProducto { get; set; }
        public string? CodigoImpuestoSri { get; set; }
    }
}
