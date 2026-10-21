using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_MOVIMIENTO")]
public sealed class EContaxInventarioMovimiento
{
    [Key]
    public long IdMovimiento { get; set; }

    public int CodigoProducto { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }

    [MaxLength(20)]
    public string TipoMovimiento { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
    [MaxLength(80)] public string? NumeroLote { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public decimal SaldoCantidad { get; set; }
    public decimal SaldoCosto { get; set; }

    [MaxLength(20)]
    public string MetodoValoracion { get; set; } = "PROMEDIO";

    [MaxLength(30)]
    public string? ReferenciaTipo { get; set; }

    [MaxLength(100)]
    public string? Referencia { get; set; }

    public int IdUsuario { get; set; }
    public DateTime FechaMovimiento { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Observacion { get; set; }
}
