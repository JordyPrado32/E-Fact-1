using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_LIQUIDACION_NOTIFICACION")]
public sealed class AliadoLiquidacionNotificacion
{
    [Key] public int IdNotificacion { get; set; }
    public int IdLiquidacion { get; set; }
    [MaxLength(40)] public string Tipo { get; set; } = string.Empty;
    public DateTime FechaEnvio { get; set; } = DateTime.Now;
}
