using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("FACTURA_CONVERSACION_IA", Schema = "dbo")]
public sealed class FacturaConversationSnapshot
{
    [Key]
    [Column("ID")]
    public long Id { get; set; }

    [Column("ID_USUARIO")]
    public int IdUsuario { get; set; }

    [Required]
    [StringLength(120)]
    [Column("SESSION_ID")]
    public string SessionId { get; set; } = string.Empty;

    [Required]
    [Column("ESTADO_JSON", TypeName = "nvarchar(max)")]
    public string EstadoJson { get; set; } = "{}";

    [Column("ESTADO_VERSION")]
    public long EstadoVersion { get; set; }

    [Column("ACTUALIZADO_EN")]
    public DateTimeOffset ActualizadoEn { get; set; }

    [Column("EXPIRA_EN")]
    public DateTimeOffset ExpiraEn { get; set; }
}
