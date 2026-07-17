using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("USU_SOLICITUD_OBSERVACION")]
public class UsuSolicitudObservacion
{
    [Key]
    [Column("OBS_ID")]
    public int ObsId { get; set; }

    [Column("OBS_ID_SOLICITUD")]
    public int ObsIdSolicitud { get; set; }

    [Column("OBS_ID_DOCUMENTO")]
    public int? ObsIdDocumento { get; set; }

    [Column("OBS_CAMPO_OBSERVADO")]
    public string ObsCampoObservedo { get; set; } = string.Empty;

    [Column("OBS_TIPO")]
    public string ObsTipo { get; set; } = string.Empty;

    [Column("OBS_DETALLE")]
    public string ObsDetalle { get; set; } = string.Empty;

    [Column("OBS_ESTADO")]
    public string ObsEstado { get; set; } = "PENDIENTE";

    [Column("OBS_TOKEN_CORRECCION")]
    public string ObsTokenCorreccion { get; set; } = string.Empty;

    [Column("OBS_FECHA_EXPIRACION_TOKEN")]
    public DateTime? ObsFechaExpiracionToken { get; set; }

    [Column("OBS_FECHA_OBSERVACION")]
    public DateTime ObsFechaObservacion { get; set; } = DateTime.Now;

    [Column("OBS_ID_USUARIO_SOPORTE")]
    public int? ObsIdUsuarioSoporte { get; set; }

    [Column("OBS_RESPUESTA_USUARIO")]
    public string ObsRespuestaUsuario { get; set; } = string.Empty;

    [Column("OBS_ACTIVO")]
    public bool ObsActivo { get; set; } = true;
}
