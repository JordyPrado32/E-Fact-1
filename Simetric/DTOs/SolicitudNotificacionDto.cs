namespace Simetric.DTOs;

public sealed class SolicitudNotificacionDto
{
    public int ObsId { get; set; }
    public int SolId { get; set; }
    public string ObsTipo { get; set; } = string.Empty;
    public string ObsCampoObservado { get; set; } = string.Empty;
    public string ObsDetalle { get; set; } = string.Empty;
    public string ObsRespuestaUsuario { get; set; } = string.Empty;
    public string Destino { get; set; } = "CLIENTE";
    public DateTime ObsFechaObservacion { get; set; }
    public string SolIdentificacion { get; set; } = string.Empty;
    public string SolTipoIdentificacion { get; set; } = string.Empty;
    public string SolNombres { get; set; } = string.Empty;
    public string SolPrimerApellido { get; set; } = string.Empty;
    public string SolSegundoApellido { get; set; } = string.Empty;
}
