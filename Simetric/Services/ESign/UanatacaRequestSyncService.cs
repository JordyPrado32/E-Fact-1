using Microsoft.Extensions.DependencyInjection;

namespace Simetric.Services.ESign;

public sealed class UanatacaRequestSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UanatacaRequestSyncService> _logger;

    public UanatacaRequestSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<UanatacaRequestSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SolicitudService>();
                await service.SincronizarTodasLasSolicitudesUanatacaPorUuidAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en la sincronización automática de solicitudes Uanataca.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
