namespace Simetric.Services;

public sealed class AuditService
{
    private readonly SqlAuditService _sqlAuditService;
    private readonly ILogger<AuditService> _logger;

    public AuditService(SqlAuditService sqlAuditService, ILogger<AuditService> logger)
    {
        _sqlAuditService = sqlAuditService;
        _logger = logger;
    }

    public bool IsEnabled => _sqlAuditService.IsEnabled;

    public async Task RegistrarAuditoriaAsync(
        int? idUsuario,
        string accion,
        object? valoresPreviosObj,
        object? valorNuevoObj,
        object? detallesObj,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        await _sqlAuditService.RegistrarAuditoriaAsync(
            idUsuario,
            accion,
            valoresPreviosObj,
            valorNuevoObj,
            detallesObj,
            cancellationToken);
    }

    public async Task<bool> TryRegistrarAuditoriaAsync(
        int? idUsuario,
        string accion,
        object? valoresPreviosObj,
        object? valorNuevoObj,
        object? detallesObj,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        try
        {
            await RegistrarAuditoriaAsync(
                idUsuario,
                accion,
                valoresPreviosObj,
                valorNuevoObj,
                detallesObj,
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AUDITORIA_FALLO_ACCION {Accion}", accion);
            return false;
        }
    }
}
