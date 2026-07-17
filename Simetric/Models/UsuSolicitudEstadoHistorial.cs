using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("USU_SOLICITUD_ESTADO_HISTORIAL")]
    public class UsuSolicitudEstadoHistorial
    {
        [Key]
        [Column("HIS_ID")]
        public int HisId { get; set; }

        [Column("HIS_ID_SOLICITUD")]
        public int HisIdSolicitud { get; set; }

        [Required]
        [StringLength(20)]
        [Column("HIS_ORIGEN_ESTADO")]
        public string HisOrigenEstado { get; set; } = string.Empty; // NUMERICA, UANATACA

        [Column("HIS_ID_ESTADO_ANTERIOR")]
        public int? HisIdEstadoAnterior { get; set; }

        [Column("HIS_ID_ESTADO_NUEVO")]
        public int HisIdEstadoNuevo { get; set; }

        [StringLength(500)]
        [Column("HIS_COMENTARIO")]
        public string HisComentario { get; set; } = string.Empty;

        [Column("HIS_FECHA_CAMBIO")]
        public DateTime HisFechaCambio { get; set; } = DateTime.Now;

        [Column("HIS_ID_USUARIO_RESPONSABLE")]
        public int? HisIdUsuarioResponsable { get; set; }

        [ForeignKey("HisIdSolicitud")]
        public virtual UsuSolicitudFirma Solicitud { get; set; } = null!;

        [ForeignKey("HisIdEstadoAnterior")]
        public virtual UsuEstadoFirma EstadoAnterior { get; set; } = null!;

        [ForeignKey("HisIdEstadoNuevo")]
        public virtual UsuEstadoFirma EstadoNuevo { get; set; } = null!;
    }
}
