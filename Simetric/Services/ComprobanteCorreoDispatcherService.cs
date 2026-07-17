using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace Simetric.Services;

public sealed class ComprobanteCorreoDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ComprobanteCorreoDispatcherService> _logger;
    private readonly TimeSpan _intervalo;
    private readonly TimeSpan _intervaloErrorConexion;

    public ComprobanteCorreoDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<ComprobanteCorreoDispatcherService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var segundos = configuration.GetValue<int?>("EmailComprobantes:DispatcherIntervalSeconds") ?? 15;
        var segundosErrorConexion = configuration.GetValue<int?>("EmailComprobantes:DispatcherConnectionErrorIntervalSeconds") ?? 90;
        _intervalo = TimeSpan.FromSeconds(Math.Max(5, segundos));
        _intervaloErrorConexion = TimeSpan.FromSeconds(Math.Max(segundos, segundosErrorConexion));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var demoraSiguienteCiclo = _intervalo;
            try
            {
                await ProcesarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (EsErrorConexionSql(ex))
                {
                    demoraSiguienteCiclo = _intervaloErrorConexion;
                    _logger.LogWarning(ex, "Error de conexion SQL en el despachador automatico. Se reintentara en {Segundos} segundos.", (int)demoraSiguienteCiclo.TotalSeconds);
                }
                else
                {
                    _logger.LogError(ex, "Error en el despachador automatico de comprobantes electronicos.");
                }
            }

            try
            {
                await Task.Delay(demoraSiguienteCiclo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static bool EsErrorConexionSql(Exception ex)
    {
        while (ex != null)
        {
            if (ex is SqlException sqlEx && sqlEx.Number is
                -2 or 53 or 64 or 233 or 10053 or 10054 or 10060 or 10061 or 10065 or 11001)
            {
                return true;
            }

            if (ex is TimeoutException)
                return true;

            ex = ex.InnerException!;
        }

        return false;
    }

    private async Task ProcesarPendientesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var facturacionService = scope.ServiceProvider.GetRequiredService<FacturacionService>();
        var notaCreditoService = scope.ServiceProvider.GetRequiredService<NotaCreditoService>();
        var retencionCorreoService = scope.ServiceProvider.GetRequiredService<RetencionCorreoService>();
        var notaDebitoService = scope.ServiceProvider.GetRequiredService<NotaDebitoService>();
        var guiaRemisionService = scope.ServiceProvider.GetRequiredService<GuiaRemisionService>();
        var liquidacionCompraService = scope.ServiceProvider.GetRequiredService<LiquidacionCompraService>();

        var facturasPendientes = await facturacionService.GetFacturasAutorizadasPendientesCorreoAsync(10);
        foreach (var idFactura in facturasPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await facturacionService.IntentarEnviarFacturaPorCorreoAsync(idFactura);
            if (resultado.Enviado)
                _logger.LogInformation("Factura {IdFactura} enviada automaticamente por correo.", idFactura);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la factura {IdFactura}: {Mensaje}", idFactura, resultado.Mensaje);
        }

        var notasPendientes = await notaCreditoService.GetNotasCreditoAutorizadasPendientesCorreoAsync(10);
        foreach (var secNota in notasPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await notaCreditoService.IntentarEnviarNotaCreditoPorCorreoAsync(secNota);
            if (resultado.Enviado)
                _logger.LogInformation("Nota de credito {SecNota} enviada automaticamente por correo.", secNota);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la nota de credito {SecNota}: {Mensaje}", secNota, resultado.Mensaje);
        }

        var retencionesPendientes = await retencionCorreoService.GetRetencionesAutorizadasPendientesCorreoAsync(10);
        foreach (var secRetencion in retencionesPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await retencionCorreoService.IntentarEnviarRetencionPorCorreoAsync(secRetencion);
            if (resultado.Enviado)
                _logger.LogInformation("Retencion {SecRetencion} enviada automaticamente por correo.", secRetencion);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la retencion {SecRetencion}: {Mensaje}", secRetencion, resultado.Mensaje);
        }

        var notasDebitoPendientes = await notaDebitoService.GetNotasDebitoAutorizadasPendientesCorreoAsync(10);
        foreach (var secNotaDebito in notasDebitoPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await notaDebitoService.IntentarEnviarNotaDebitoPorCorreoAsync(secNotaDebito);
            if (resultado.Enviado)
                _logger.LogInformation("Nota de debito {SecNotaDebito} enviada automaticamente por correo.", secNotaDebito);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la nota de debito {SecNotaDebito}: {Mensaje}", secNotaDebito, resultado.Mensaje);
        }

        var guiasPendientes = await guiaRemisionService.GetGuiasRemisionAutorizadasPendientesCorreoAsync(10);
        foreach (var secGuia in guiasPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await guiaRemisionService.IntentarEnviarGuiaRemisionPorCorreoAsync(secGuia);
            if (resultado.Enviado)
                _logger.LogInformation("Guia de remision {SecGuia} enviada automaticamente por correo.", secGuia);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la guia de remision {SecGuia}: {Mensaje}", secGuia, resultado.Mensaje);
        }

        var liquidacionesPendientes = await liquidacionCompraService.GetLiquidacionesAutorizadasPendientesCorreoAsync(10);
        foreach (var codFactura in liquidacionesPendientes)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var resultado = await liquidacionCompraService.IntentarEnviarLiquidacionPorCorreoAsync(codFactura);
            if (resultado.Enviado)
                _logger.LogInformation("Liquidacion de compra {CodFactura} enviada automaticamente por correo.", codFactura);
            else if (resultado.Error)
                _logger.LogWarning("No se pudo enviar automaticamente la liquidacion de compra {CodFactura}: {Mensaje}", codFactura, resultado.Mensaje);
        }
    }
}
