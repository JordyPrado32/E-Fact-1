using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_LIQUIDACION")]
public sealed class AliadoLiquidacion
{
    [Key]
    public int IdLiquidacion { get; set; }
    public int IdVendedor { get; set; }
    [Required, MaxLength(20)] public string Periodo { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime Fecha { get; set; }
    [Required, MaxLength(30)] public string Estado { get; set; } = "Pendiente";
    [MaxLength(100)] public string? ReferenciaPago { get; set; }
}
