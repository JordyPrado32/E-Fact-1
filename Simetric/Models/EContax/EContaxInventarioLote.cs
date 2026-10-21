using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_LOTE")]
public sealed class EContaxInventarioLote
{
    [Key]
    public long IdLote { get; set; }

    public int CodigoProducto { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public decimal CantidadOriginal { get; set; }
    public decimal CantidadDisponible { get; set; }
    public decimal CostoUnitario { get; set; }
    [MaxLength(80)]
    public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public DateTime FechaIngreso { get; set; } = DateTime.UtcNow;
    public long? IdMovimientoEntrada { get; set; }
}
