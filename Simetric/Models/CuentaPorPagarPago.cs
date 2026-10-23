using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("CXP_PAGOS")]
public sealed class CuentaPorPagarPago
{
    [Key]
    [Column("idPago")]
    public int IdPago { get; set; }

    [Column("codFactura")]
    public int CodFactura { get; set; }

    [Column("monto")]
    public decimal Monto { get; set; }

    [Column("formaPago")]
    public string? FormaPago { get; set; }

    [Column("referencia")]
    public string? Referencia { get; set; }

    [Column("fechaPago")]
    public DateTime FechaPago { get; set; }

    [Column("idUsuario")]
    public int? IdUsuario { get; set; }

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("estado")]
    public bool Estado { get; set; } = true;
}
