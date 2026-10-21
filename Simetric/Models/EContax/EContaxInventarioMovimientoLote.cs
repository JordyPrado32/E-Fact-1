using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_MOVIMIENTO_LOTE")]
public sealed class EContaxInventarioMovimientoLote
{
    [Key]
    public long IdMovimientoLote { get; set; }

    public long IdMovimiento { get; set; }
    public long IdLote { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
}
