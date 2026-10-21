using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_EXISTENCIA")]
public sealed class EContaxInventarioExistencia
{
    public int CodigoProducto { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public decimal StockActual { get; set; }
    public decimal StockReservado { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal StockMaximo { get; set; }
    public decimal CostoPromedio { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
