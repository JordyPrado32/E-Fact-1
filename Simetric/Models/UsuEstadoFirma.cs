using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("USU_ESTADO_FIRMA")]
    public class UsuEstadoFirma
    {
        [Key]
        [Column("EST_ID")]
        public int EstId { get; set; }

        [Column("EST_NOMBRE")]
        public string EstNombre { get; set; } = string.Empty;

        [Column("EST_ORIGEN")]
        public string EstOrigen { get; set; } = string.Empty; // "NUMERICA" o "UANATACA"

        [Column("EST_ACTIVO")]
        public bool EstActivo { get; set; }

        [Column("EST_FECHA_REGISTRO")]
        public DateTime EstFechaRegistro { get; set; }

        // --- PROPIEDADES DE NAVEGACIÓN (Esto es lo que falta para quitar el error) ---

        public virtual ICollection<UsuSolicitudFirma> SolicitudesNumerica { get; set; } = new List<UsuSolicitudFirma>();
        public virtual ICollection<UsuSolicitudFirma> SolicitudesUanataca { get; set; } = new List<UsuSolicitudFirma>();

        // Estas son las que el ModelBuilder no encontraba:
        public virtual ICollection<UsuSolicitudEstadoHistorial> HistorialAnterior { get; set; } = new List<UsuSolicitudEstadoHistorial>();
        public virtual ICollection<UsuSolicitudEstadoHistorial> HistorialNuevo { get; set; } = new List<UsuSolicitudEstadoHistorial>();
    }
}