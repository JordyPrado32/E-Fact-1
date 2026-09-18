using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed record SolicitudFirmaBorrador(string Id, string Titulo, DateTime FechaGuardado, string DatosJson);

public sealed class SolicitudFirmaBorradorService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SolicitudFirmaBorradorService(IDbContextFactory<AppDbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaEnsured) return;
        await SchemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured) return;
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[dbo].[ESIGN_SOLICITUD_BORRADOR]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ESIGN_SOLICITUD_BORRADOR]
                    (
                        [BOR_ID] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_ESIGN_SOLICITUD_BORRADOR] PRIMARY KEY,
                        [BOR_ID_USUARIO] INT NOT NULL,
                        [BOR_TITULO] NVARCHAR(250) NOT NULL,
                        [BOR_DATOS_JSON] NVARCHAR(MAX) NOT NULL,
                        [BOR_FECHA_GUARDADO] DATETIME2 NOT NULL
                    );
                    CREATE INDEX [IX_ESIGN_SOLICITUD_BORRADOR_USUARIO_FECHA]
                        ON [dbo].[ESIGN_SOLICITUD_BORRADOR] ([BOR_ID_USUARIO], [BOR_FECHA_GUARDADO] DESC);
                END;
                """, cancellationToken);
            _schemaEnsured = true;
        }
        finally { SchemaLock.Release(); }
    }

    public async Task<IReadOnlyList<SolicitudFirmaBorrador>> ObtenerAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQuery<SolicitudFirmaBorrador>($"""
            SELECT CONVERT(varchar(36), [BOR_ID]) AS [Id], [BOR_TITULO] AS [Titulo],
                   [BOR_FECHA_GUARDADO] AS [FechaGuardado], [BOR_DATOS_JSON] AS [DatosJson]
            FROM [dbo].[ESIGN_SOLICITUD_BORRADOR]
            WHERE [BOR_ID_USUARIO] = {userId}
            ORDER BY [BOR_FECHA_GUARDADO] DESC
            """).ToListAsync(cancellationToken);
    }

    public async Task<SolicitudFirmaBorrador> GuardarAsync(int userId, string titulo, string datosJson, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        var id = Guid.NewGuid();
        var fecha = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [dbo].[ESIGN_SOLICITUD_BORRADOR] ([BOR_ID], [BOR_ID_USUARIO], [BOR_TITULO], [BOR_DATOS_JSON], [BOR_FECHA_GUARDADO])
            VALUES ({id}, {userId}, {titulo.Trim()}, {datosJson}, {fecha});
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [dbo].[ESIGN_SOLICITUD_BORRADOR]
            WHERE [BOR_ID_USUARIO] = {userId} AND [BOR_ID] NOT IN
            (
                SELECT [BOR_ID] FROM
                (
                    SELECT TOP (20) [BOR_ID] FROM [dbo].[ESIGN_SOLICITUD_BORRADOR]
                    WHERE [BOR_ID_USUARIO] = {userId}
                    ORDER BY [BOR_FECHA_GUARDADO] DESC
                ) AS recientes
            );
            """, cancellationToken);
        return new SolicitudFirmaBorrador(id.ToString(), titulo.Trim(), fecha, datosJson);
    }

    public async Task<bool> EliminarAsync(int userId, string id, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var guid)) return false;
        await EnsureSchemaAsync(cancellationToken);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [dbo].[ESIGN_SOLICITUD_BORRADOR] WHERE [BOR_ID] = {guid} AND [BOR_ID_USUARIO] = {userId};", cancellationToken) > 0;
    }
}
