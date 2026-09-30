using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_COMISION_PARAMETRO")]
public sealed class AliadoComisionParametro
{
    [Key] public int IdParametro { get; set; }
    public int IdVendedor { get; set; }
    [Required, MaxLength(7)] public string Periodo { get; set; } = string.Empty;
    public decimal Porcentaje { get; set; }
    public decimal PorcentajeHastaMil { get; set; }
    public decimal PorcentajeDesdeMil { get; set; }
    public DateTime FechaActualizacion { get; set; }
    public int IdUsuarioActualizacion { get; set; }
}
