using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("NOTIFICACION_DESCARTADA", Schema = "dbo")]
public sealed class NotificacionDescartada
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("IdUsuario")]
    public int IdUsuario { get; set; }

    [Column("NotificacionId")]
    [StringLength(200)]
    public string NotificacionId { get; set; } = string.Empty;

    [Column("FechaDescarte")]
    public DateTime FechaDescarte { get; set; }
}
