using Simetric.Modules.AsistenteIAFacturacion.DTOs;
using Simetric.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IFacturacionService
{
    Task<IReadOnlyList<FacturaListDto>> ListarFacturasUsuarioAsync(int userId, int top = 200, CancellationToken cancellationToken = default);
    // TODO: Conectar con el servicio real de emisión de facturas
    Task<IReadOnlyList<string>> ObtenerFormasPagoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FacturaReferenciaDto>> BuscarFacturasParaNotaCreditoAsync(int userId, string query, CancellationToken cancellationToken = default);
    Task<FacturaEmissionResult> EmitirAsync(int userId, FacturaDraftDto draft, string? requestId = null, CancellationToken cancellationToken = default);
    Task<NotaCreditoEmissionResult> EmitirNotaCreditoAsync(int userId, int facturaId, string? motivo = null, CancellationToken cancellationToken = default);
    Task<NotaDebitoEmissionResult> EmitirNotaDebitoAsync(
        int userId,
        string referenciaFactura,
        string motivo,
        decimal valor,
        decimal tarifaPorcentaje = 0,
        CancellationToken cancellationToken = default);
    Task<GuiaEmissionResult> EmitirGuiaDesdeFacturaAsync(
        int userId,
        string referenciaFactura,
        string transportistaIdentificacion,
        string transportistaRazonSocial,
        string placa,
        string direccionPartida,
        string destinatarioIdentificacion,
        string destinatarioRazonSocial,
        string direccionDestino,
        string motivoTraslado = "VENTA",
        CancellationToken cancellationToken = default);
    Task<LiquidacionEmissionResult> EmitirLiquidacionCompraAsync(
        int userId,
        string tipoIdentificacionProveedor,
        string identificacionProveedor,
        string razonSocialProveedor,
        string direccionProveedor,
        string descripcion,
        decimal cantidad,
        decimal precioUnitario,
        decimal tarifaPorcentaje,
        string formaPago,
        string? emailProveedor = null,
        int? plazo = null,
        CancellationToken cancellationToken = default);
    Task<RetencionEmissionResult> EmitirRetencionAsync(
        int userId,
        string referenciaRetencion,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentoConsultaDto>> ConsultarDocumentosAsync(
        int userId,
        string? tipo = null,
        string? filtro = null,
        string? periodo = null,
        int limite = 10,
        CancellationToken cancellationToken = default);
    Task<ESignEstadoDto> ConsultarESignEstadoAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ESignSolicitudDto>> ConsultarESignSolicitudesAsync(
        int userId,
        string? filtro = null,
        string? estado = null,
        int limite = 10,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ESignDocumentoDto>> ConsultarESignDocumentosAsync(
        int userId,
        string? filtro = null,
        int limite = 10,
        CancellationToken cancellationToken = default);
    Task<ESignSincronizacionDto> SincronizarESignSolicitudAsync(
        int userId,
        int solicitudId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ESignPlanDto>> ConsultarESignPlanesAsync(CancellationToken cancellationToken = default);
}

public sealed class FacturaEmissionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroFactura { get; set; }
}

public sealed class NotaCreditoEmissionResult
{
    public bool Success { get; set; }
    public bool Autorizada { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroNotaCredito { get; set; }
}

public sealed class NotaDebitoEmissionResult
{
    public bool Success { get; set; }
    public bool Autorizada { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroNotaDebito { get; set; }
}

public sealed class GuiaEmissionResult
{
    public bool Success { get; set; }
    public bool Autorizada { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroGuia { get; set; }
}

public sealed class LiquidacionEmissionResult
{
    public bool Success { get; set; }
    public bool Autorizada { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroLiquidacion { get; set; }
    public string? XmlUrl { get; set; }
    public string? PdfUrl { get; set; }
}

public sealed class RetencionEmissionResult
{
    public bool Success { get; set; }
    public bool Autorizada { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NumeroRetencion { get; set; }
    public string? XmlUrl { get; set; }
    public string? PdfUrl { get; set; }
}

public sealed class DocumentoConsultaDto
{
    public string Tipo { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public string Tercero { get; set; } = string.Empty;
    public string Identificacion { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string NumeroAutorizacion { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string XmlUrl { get; set; } = string.Empty;
    public string PdfUrl { get; set; } = string.Empty;
}

public sealed class ESignEstadoDto
{
    public bool Configurado { get; set; }
    public bool TieneCertificado { get; set; }
    public bool TieneClave { get; set; }
    public bool EsConfiguracionCuenta { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

public sealed class ESignSolicitudDto
{
    public int Id { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string FormatoFirma { get; set; } = string.Empty;
    public string Vigencia { get; set; } = string.Empty;
    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public bool PagoExitoso { get; set; }
    public decimal? MontoPago { get; set; }
    public string? EstadoProveedor { get; set; }
    public string? DetalleProveedor { get; set; }
    public int ObservacionesPendientes { get; set; }
    public bool TieneCertificadoEmitido { get; set; }
}

public sealed class ESignDocumentoDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = "PDF firmado";
    public long TamanoBytes { get; set; }
    public DateTime FechaActualizacion { get; set; }
    public string Url { get; set; } = string.Empty;
}

public sealed class ESignSincronizacionDto
{
    public bool Success { get; set; }
    public bool Created { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? EstadoProveedor { get; set; }
}

public sealed class ESignPlanDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public decimal Precio { get; set; }
}
