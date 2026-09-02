using Microsoft.Extensions.DependencyInjection;

namespace Simetric.Services.ESign;

public sealed class UanatacaRequestSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UanatacaRequestSyncService> _logger;

    public UanatacaRequestSyncService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<UanatacaRequestSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = Math.Max(60,
            _configuration.GetValue<int?>("UanatacaApi:RequestSyncIntervalSeconds") ?? 300);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SolicitudService>();
                await service.SincronizarSolicitudesExternasUanatacaAsync(stoppingToken);
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
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
