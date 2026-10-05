using Microsoft.Extensions.Hosting;

namespace Simetric.Services;

public sealed class AliadoPortalBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AliadoPortalBackgroundService> _logger;

    public AliadoPortalBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AliadoPortalBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            await EjecutarCicloAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EjecutarCicloAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var portal = scope.ServiceProvider.GetRequiredService<AliadoPortalService>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
            await portal.SincronizarComisionesPortalAsync();
            await portal.CerrarPeriodosComisionesAsync();

            foreach (var avisoFactura in await portal.ObtenerAvisosFacturaLiquidacionAsync())
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(avisoFactura.Email))
                    await email.EnviarAvisoFacturaLiquidacionAsync(avisoFactura.Email, avisoFactura.NombreAliado, avisoFactura.Periodo, avisoFactura.Total, avisoFactura.EmisorRazonSocial, avisoFactura.EmisorRuc, avisoFactura.EmisorDireccion, avisoFactura.EmisorEmail, avisoFactura.EmisorTelefono);
            }

            if (!_configuration.GetValue("Aliados:NotificacionesRenovacionHabilitadas", true))
                return;

            foreach (var aviso in await portal.ObtenerNotificacionesRenovacionAsync())
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(aviso.Email))
                    continue;
                await email.EnviarAvisoRenovacionAliadoAsync(aviso.Email, aviso.NombreAliado, aviso.Cliente, aviso.Producto, aviso.FechaVencimiento, aviso.DiasRestantes);
                await portal.RegistrarNotificacionRenovacionAsync(aviso.IdVendedor, aviso.IdFactura, aviso.Tipo);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el ciclo automático del Portal de Aliados.");
        }
    }
}
