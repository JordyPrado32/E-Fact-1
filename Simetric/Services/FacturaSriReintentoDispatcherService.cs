using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Simetric.Services;

public sealed class FacturaSriReintentoDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FacturaSriReintentoDispatcherService> _logger;
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(8);

    public FacturaSriReintentoDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<FacturaSriReintentoDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en el reintento automatico de facturas SRI.");
            }

            try
            {
                await Task.Delay(Intervalo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcesarAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var facturacionService = scope.ServiceProvider.GetRequiredService<FacturacionService>();

        var vencidas = await facturacionService.GetFacturasVencidasReintentoSriAsync(10);
        foreach (var idFactura in vencidas)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await facturacionService.MarcarFacturaSriRechazadaAsync(
                idFactura,
                DocumentoAutorizacionHelper.EstadoNoAutorizado,
                "Factura rechazada automaticamente porque el SRI no autorizo dentro de las primeras 24 horas.");
            _logger.LogWarning("Factura {IdFactura} marcada como no autorizada por vencimiento de reintentos SRI.", idFactura);
        }

        var pendientes = await facturacionService.GetFacturasPendientesReintentoSriAsync(10);
        foreach (var idFactura in pendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await facturacionService.ReintentarEnvioSriFacturaAsync(idFactura, esReintentoAutomatico: true);
            if (string.Equals(resultado.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Factura {IdFactura} autorizada automaticamente tras reintento SRI.", idFactura);
            }
            else if (string.Equals(resultado.estado, DocumentoAutorizacionHelper.EstadoNoAutorizado, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Factura {IdFactura} rechazada por el SRI al reintentar: {Mensaje}", idFactura, resultado.mensaje);
            }
            else if (string.Equals(resultado.estado, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Factura {IdFactura} sigue pendiente por error transitorio SRI: {Mensaje}", idFactura, resultado.mensaje);
            }
            else if (string.Equals(resultado.estado, "LIMITE_DIARIO", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Factura {IdFactura} quedo pendiente para reenvio manual tras alcanzar el limite diario.", idFactura);
            }
        }
    }
}
