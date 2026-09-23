using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_COMISION")]
public sealed class AliadoComision
{
    [Key]
    public int IdComision { get; set; }

    public int IdVendedor { get; set; }
    public int IdFactura { get; set; }
    public int? IdCliente { get; set; }

    [Required, MaxLength(40)]
    public string TipoComision { get; set; } = string.Empty;

    public decimal BaseComisionable { get; set; }
    public decimal Porcentaje { get; set; }
    public decimal Valor { get; set; }

    [Required, MaxLength(30)]
    public string Estado { get; set; } = "Generada";

    public DateTime FechaGeneracion { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaPago { get; set; }
    public int? IdLiquidacion { get; set; }
    public int? IdUsuarioAprobacion { get; set; }
    public int? IdUsuarioPago { get; set; }
}
