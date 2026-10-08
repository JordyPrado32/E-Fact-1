using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("VENDEDOR_COMISION")]
public sealed class VendedorComision
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

    [Required, MaxLength(7)]
    public string Periodo { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Estado { get; set; } = "Pendiente";

    public DateTime FechaGeneracion { get; set; }
    public DateTime? FechaPago { get; set; }
    public int? IdUsuarioPago { get; set; }
}
