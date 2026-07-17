using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("COMPROBANTECORREOESTADO", Schema = "dbo")]
public class ComprobanteCorreoEstado
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("TipoDocumento")]
    [StringLength(40)]
    public string TipoDocumento { get; set; } = string.Empty;

    [Column("DocumentoId")]
    public int DocumentoId { get; set; }

    [Column("CorreoEnviado")]
    public bool CorreoEnviado { get; set; }

    [Column("FechaEnvioCorreo")]
    public DateTime? FechaEnvioCorreo { get; set; }

    [Column("UltimoErrorCorreo")]
    [StringLength(1000)]
    public string? UltimoErrorCorreo { get; set; }

    [Column("FechaRegistro")]
    public DateTime FechaRegistro { get; set; }

    [Column("FechaActualizacion")]
    public DateTime FechaActualizacion { get; set; }
}
