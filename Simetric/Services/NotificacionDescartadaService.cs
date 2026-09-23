using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed class NotificacionDescartadaService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public NotificacionDescartadaService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<HashSet<string>> ObtenerIdsAsync(int usuarioId, CancellationToken cancellationToken = default)
    {
        if (usuarioId <= 0) return new HashSet<string>(StringComparer.Ordinal);
        await EnsureSchemaAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return (await db.Set<NotificacionDescartada>().AsNoTracking()
            .Where(x => x.IdUsuario == usuarioId)
            .Select(x => x.NotificacionId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public async Task DescartarAsync(int usuarioId, IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var values = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Where(x => x.Length <= 200)
            .Distinct(StringComparer.Ordinal).Take(200).ToList();
        if (usuarioId <= 0 || values.Count == 0) return;

        await EnsureSchemaAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existentes = await db.Set<NotificacionDescartada>()
            .Where(x => x.IdUsuario == usuarioId && values.Contains(x.NotificacionId))
            .Select(x => x.NotificacionId).ToListAsync(cancellationToken);
        var ahora = DateTime.Now;
        db.AddRange(values.Except(existentes, StringComparer.Ordinal).Select(id => new NotificacionDescartada
        {
            IdUsuario = usuarioId,
            NotificacionId = id,
            FechaDescarte = ahora
        }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaEnsured) return;
        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured) return;
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.NOTIFICACION_DESCARTADA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NOTIFICACION_DESCARTADA (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NOTIFICACION_DESCARTADA PRIMARY KEY,
        IdUsuario INT NOT NULL,
        NotificacionId NVARCHAR(200) NOT NULL,
        FechaDescarte DATETIME NOT NULL CONSTRAINT DF_NOTIFICACION_DESCARTADA_FechaDescarte DEFAULT(GETDATE())
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NOTIFICACION_DESCARTADA_Usuario_Id' AND object_id = OBJECT_ID('dbo.NOTIFICACION_DESCARTADA'))
    CREATE UNIQUE INDEX UX_NOTIFICACION_DESCARTADA_Usuario_Id ON dbo.NOTIFICACION_DESCARTADA(IdUsuario, NotificacionId);
""", cancellationToken);
            _schemaEnsured = true;
        }
        finally { SchemaLock.Release(); }
    }
}
