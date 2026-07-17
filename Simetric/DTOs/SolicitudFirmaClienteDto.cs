namespace Simetric.DTOs;

public sealed class SolicitudFirmaClienteDto
{
    public int SolId { get; set; }
    public string SolNombres { get; set; } = string.Empty;
    public string SolPrimerApellido { get; set; } = string.Empty;
    public string? SolSegundoApellido { get; set; }
    public string SolTipoIdentificacion { get; set; } = string.Empty;
    public string SolIdentificacion { get; set; } = string.Empty;
    public DateTime SolFechaSolicitud { get; set; }
    public DateTime? SolFechaAprobacion { get; set; }
    public DateTime? SolFechaActualizacion { get; set; }
    public string SolFormatoFirma { get; set; } = string.Empty;
    public string SolVigencia { get; set; } = string.Empty;
    public bool SolPagoExitoso { get; set; }
    public DateTime? SolFechaPago { get; set; }
    public string? SolIdTransaccionPago { get; set; }
    public string? EstadoSolicitud { get; set; }
    public bool TieneArchivoP12 { get; set; }
    public int TamanoArchivoProtegido { get; set; }
    public string? ClaveP12 { get; set; }
}
