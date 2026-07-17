namespace Simetric.DTOs;

public sealed class SolicitudPagoClienteDto
{
    public int SolId { get; set; }
    public int SolIdUsuarioCliente { get; set; }
    public int SolIdEstadoNumerica { get; set; }
    public string? EstadoSolicitud { get; set; }
    public string SolNombres { get; set; } = string.Empty;
    public string SolPrimerApellido { get; set; } = string.Empty;
    public string? SolSegundoApellido { get; set; }
    public string SolCorreo1 { get; set; } = string.Empty;
    public string SolTipoIdentificacion { get; set; } = string.Empty;
    public string SolIdentificacion { get; set; } = string.Empty;
    public DateTime SolFechaSolicitud { get; set; }
    public string SolFormatoFirma { get; set; } = string.Empty;
    public string SolVigencia { get; set; } = string.Empty;
    public decimal? SolMontoPago { get; set; }
    public bool SolPagoExitoso { get; set; }
    public string? SolIdTransaccionPago { get; set; }
    public DateTime? SolFechaPago { get; set; }
    public int ObservacionesPendientes { get; set; }
    public string? UltimaNotificacion { get; set; }
    public DateTime? FechaUltimaNotificacion { get; set; }
}
