using Simetric.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("USU_SOLICITUD_DOCUMENTO")]
public class UsuSolicitudDocumento
{
    [Key]
    [Column("DOC_ID")]
    public int DocId { get; set; }

    [Column("DOC_ID_SOLICITUD")]
    public int DocIdSolicitud { get; set; }

    [Required]
    [Column("DOC_TIPO")]
    public string DocTipo { get; set; } = string.Empty;

    [Required]
    [Column("DOC_NOMBRE_ARCHIVO")]
    public string DocNombreArchivo { get; set; } = string.Empty;

    [Required]
    [Column("DOC_RUTA_ARCHIVO")]
    public string DocRutaArchivo { get; set; } = string.Empty;

    [Required]
    [Column("DOC_EXTENSION")]
    public string DocExtension { get; set; } = string.Empty;

    [Column("DOC_TAMANO_ARCHIVO")]
    public long? DocTamanoArchivo { get; set; }

    [Column("DOC_VERSION")]
    public int DocVersion { get; set; } = 1;

    [Column("DOC_VIGENTE")]
    public bool DocVigente { get; set; } = true;

    [Column("DOC_OBSERVACION")]
    public string DocObservacion { get; set; } = string.Empty;

    [Column("DOC_FECHA_CARGA")]
    public DateTime DocFechaCarga { get; set; } = DateTime.Now;

    [ForeignKey("DocIdSolicitud")]
    public virtual UsuSolicitudFirma Solicitud { get; set; } = null!;
}
