using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_RENOVACION_NOTIFICACION")]
public sealed class AliadoRenovacionNotificacion
{
    [Key]
    public int IdNotificacion { get; set; }

    public int IdVendedor { get; set; }
    public int IdFactura { get; set; }

    [MaxLength(40)]
    public string Tipo { get; set; } = string.Empty;

    public DateTime FechaEnvio { get; set; }
}
