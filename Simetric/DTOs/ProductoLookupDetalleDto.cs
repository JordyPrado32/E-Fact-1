namespace Simetric.DTOs
{
    public class ProductoLookupDetalleDto
    {
        public int Codproducto { get; set; }
        public string? Codprincipal { get; set; }
        public string? Codauxiliar { get; set; }

        public string? Descripcion { get; set; }
        public string? Categoria { get; set; }
        public string? Subcategoria { get; set; }
        public decimal PrecioUnitario { get; set; }

        public int TipoProducto { get; set; }
        public int? SubtipoProducto { get; set; }
        public decimal Costo { get; set; }
        public string? CodigoImpuestoSri { get; set; } // CODIGOIMPUESTO (SRI)
        public int TarifaIva { get; set; }             // 0, 12, etc
    }
}
