using Simetric.Modules.AsistenteIAFacturacion.DTOs;

namespace Simetric.Modules.AsistenteIAFacturacion.Services;

public interface IFacturacionService
{
    // TODO: Conectar con el servicio real de emisión de facturas
    Task<IReadOnlyList<string>> ObtenerFormasPagoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FacturaReferenciaDto>> BuscarFacturasParaNotaCreditoAsync(int userId, string query, CancellationToken cancellationToken = default);
    Task<FacturaEmissionResult> EmitirAsync(int userId, FacturaDraftDto draft, CancellationToken cancellationToken = default);
    Task<NotaCreditoEmissionResult> EmitirNotaCreditoAsync(int userId, int facturaId, string? motivo = null, CancellationToken cancellationToken = default);
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
