using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed record FirmaRenovacionInfo(
    int CodigoEmisor,
    int IdUsuario,
    string Cliente,
    string Ruc,
    string Email,
    DateTime? FechaExpiracion,
    int? DiasRestantes,
    bool EsValida,
    string Estado,
    string Mensaje,
    bool CorreoNotificado)
{
    public bool RequiereRenovacion => DiasRestantes is <= FirmaRenovacionService.UmbralRenovacionDias;

    public string AccionAuditoria =>
        $"NOTIFICACION_RENOVACION_FIRMA:{CodigoEmisor}:{FechaExpiracion:yyyyMMdd}";
}

public sealed class FirmaRenovacionService
{
    public const int UmbralRenovacionDias = 15;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EmisorCertificadoValidator _validator;

    public FirmaRenovacionService(
        IDbContextFactory<AppDbContext> dbFactory,
        EmisorCertificadoValidator validator)
    {
        _dbFactory = dbFactory;
        _validator = validator;
    }

    public async Task<FirmaRenovacionInfo?> ObtenerPorUsuarioAsync(
        int idUsuario,
        bool forzarActualizacion = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
            .FirstOrDefaultAsync(cancellationToken);

        if (usuario is null)
            return null;

        var idTitular = usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : usuario.IdUsuario;

        var emisor = await QueryEmisores(context)
            .Where(e => e.IdUsuario == idTitular)
            .OrderByDescending(e => e.Codigo)
            .FirstOrDefaultAsync(cancellationToken);

        return emisor is null
            ? null
            : await EvaluarAsync(emisor, forzarActualizacion, cancellationToken);
    }

    public async Task<List<FirmaRenovacionInfo>> ObtenerFirmasPorRenovarAsync(
        bool forzarActualizacion = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var emisores = await QueryEmisores(context)
            .OrderBy(e => e.RazonSocial)
            .ToListAsync(cancellationToken);

        var resultados = new ConcurrentBag<FirmaRenovacionInfo>();
        await Parallel.ForEachAsync(
            emisores,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            },
            async (emisor, token) =>
            {
                var resultado = await EvaluarAsync(emisor, forzarActualizacion, token);
                if (resultado?.RequiereRenovacion == true)
                    resultados.Add(resultado);
            });

        var lista = resultados
            .OrderBy(r => r.DiasRestantes ?? int.MaxValue)
            .ThenBy(r => r.Cliente)
            .ToList();

        if (lista.Count == 0)
            return lista;

        await using var auditContext = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var acciones = lista.Select(r => r.AccionAuditoria).Distinct().ToList();
        var notificadas = await auditContext.Auditorias
            .AsNoTracking()
            .Where(a => a.Accion != null && acciones.Contains(a.Accion))
            .Select(a => a.Accion!)
            .ToHashSetAsync(cancellationToken);

        return lista
            .Select(r => r with { CorreoNotificado = notificadas.Contains(r.AccionAuditoria) })
            .ToList();
    }

    private static IQueryable<Emisor> QueryEmisores(AppDbContext context) =>
        context.Emisores
            .AsNoTracking()
            .Include(e => e.Usuario)
            .Where(e =>
                e.Estado &&
                !e.EsEmisorSistema &&
                e.IdUsuario.HasValue &&
                e.PathCertificado != null &&
                e.PathCertificado != string.Empty &&
                e.ClaveCertificado != null &&
                e.ClaveCertificado != string.Empty);

    private async Task<FirmaRenovacionInfo?> EvaluarAsync(
        Emisor emisor,
        bool forzarActualizacion,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{emisor.Codigo}:{emisor.PathCertificado}";
        if (!forzarActualizacion &&
            Cache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.Now)
        {
            return cached.Value;
        }

        var validacion = await _validator.ValidarConApiAsync(emisor, cancellationToken);
        var fechaExpiracion = validacion.FechaExpiracion;
        var diasRestantes = fechaExpiracion.HasValue
            ? (fechaExpiracion.Value.Date - DateTime.Today).Days
            : validacion.DiasRestantes;

        var resultado = fechaExpiracion.HasValue
            ? new FirmaRenovacionInfo(
                emisor.Codigo,
                emisor.IdUsuario!.Value,
                emisor.RazonSocial?.Trim() ?? emisor.Usuario?.NombreCompleto ?? "Cliente sin nombre",
                emisor.Ruc?.Trim() ?? string.Empty,
                ObtenerEmail(emisor),
                fechaExpiracion,
                diasRestantes,
                validacion.IsValid,
                diasRestantes < 0 ? "CADUCADA" : diasRestantes <= UmbralRenovacionDias ? "POR RENOVAR" : "VIGENTE",
                validacion.Message,
                false)
            : null;

        Cache[cacheKey] = new CacheEntry(DateTimeOffset.Now.Add(CacheDuration), resultado);
        return resultado;
    }

    private static string ObtenerEmail(Emisor emisor) =>
        !string.IsNullOrWhiteSpace(emisor.Email)
            ? emisor.Email.Trim()
            : emisor.Usuario?.Email?.Trim() ?? string.Empty;

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, FirmaRenovacionInfo? Value);
}

public sealed class FirmaRenovacionNotificationService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(12);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirmaRenovacionNotificationService> _logger;

    public FirmaRenovacionNotificationService(
        IServiceScopeFactory scopeFactory,
        ILogger<FirmaRenovacionNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

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
                _logger.LogError(ex, "No se pudo procesar la notificacion de firmas por renovar.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }
    }

    private async Task ProcesarAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var renovacionService = scope.ServiceProvider.GetRequiredService<FirmaRenovacionService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var auditService = scope.ServiceProvider.GetRequiredService<AuditService>();
        var firmas = await renovacionService.ObtenerFirmasPorRenovarAsync(
            forzarActualizacion: true,
            cancellationToken);

        foreach (var firma in firmas.Where(f => !f.CorreoNotificado && !string.IsNullOrWhiteSpace(f.Email)))
        {
            try
            {
                await emailService.EnviarAvisoRenovacionFirmaAsync(
                    firma.Email,
                    firma.Cliente,
                    firma.Ruc,
                    firma.FechaExpiracion!.Value,
                    firma.DiasRestantes ?? 0);

                await auditService.RegistrarAuditoriaAsync(
                    firma.IdUsuario,
                    firma.AccionAuditoria,
                    null,
                    new { firma.CodigoEmisor, firma.FechaExpiracion, firma.DiasRestantes, firma.Email },
                    new { Evento = "Correo de renovacion de firma enviado", Asesora = "Brigitte" },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo enviar el aviso de renovacion de firma al emisor {CodigoEmisor}.",
                    firma.CodigoEmisor);
            }
        }
    }
}
