using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_RENOVACION_GESTION")]
public sealed class AliadoRenovacionGestion
{
    [Key]
    public int IdGestion { get; set; }

    public int IdVendedor { get; set; }
    public int IdFactura { get; set; }

    public DateTime FechaGestion { get; set; }

    [Required, MaxLength(50)]
    public string Resultado { get; set; } = string.Empty;

    [MaxLength(20)]
    public string OrigenGestion { get; set; } = "Aliado";

    [MaxLength(500)]
    public string? Observacion { get; set; }

    public DateTime? ProximoSeguimiento { get; set; }
    public int IdUsuario { get; set; }
}
