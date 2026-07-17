using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed class ComprobanteCorreoEstadoService
{
    public const string TipoFactura = "FACTURA";
    public const string TipoNotaDebito = "NOTA_DEBITO";
    public const string TipoGuiaRemision = "GUIA_REMISION";
    public const string TipoLiquidacionCompra = "LIQUIDACION_COMPRA";

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ComprobanteCorreoEstadoService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            foreach (var statement in BuildEnsureSchemaStatements())
                await context.Database.ExecuteSqlRawAsync(statement);

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<ComprobanteCorreoEstado?> GetEstadoAsync(string tipoDocumento, int documentoId)
    {
        if (documentoId <= 0 || string.IsNullOrWhiteSpace(tipoDocumento))
            return null;

        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Set<ComprobanteCorreoEstado>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TipoDocumento == tipoDocumento &&
                x.DocumentoId == documentoId);
    }

    public async Task RegistrarPendienteAsync(string tipoDocumento, int documentoId)
    {
        var estado = await GetOrCreateAsync(tipoDocumento, documentoId);
        if (estado.CorreoEnviado)
            return;

        estado.UltimoErrorCorreo = null;
        estado.FechaActualizacion = DateTime.Now;

        await using var context = await _dbFactory.CreateDbContextAsync();
        context.Attach(estado);
        context.Entry(estado).Property(x => x.UltimoErrorCorreo).IsModified = true;
        context.Entry(estado).Property(x => x.FechaActualizacion).IsModified = true;
        await context.SaveChangesAsync();
    }

    public async Task MarcarEnviadoAsync(string tipoDocumento, int documentoId)
    {
        var estado = await GetOrCreateAsync(tipoDocumento, documentoId);
        estado.CorreoEnviado = true;
        estado.FechaEnvioCorreo = DateTime.Now;
        estado.UltimoErrorCorreo = null;
        estado.FechaActualizacion = DateTime.Now;

        await using var context = await _dbFactory.CreateDbContextAsync();
        context.Attach(estado);
        context.Entry(estado).Property(x => x.CorreoEnviado).IsModified = true;
        context.Entry(estado).Property(x => x.FechaEnvioCorreo).IsModified = true;
        context.Entry(estado).Property(x => x.UltimoErrorCorreo).IsModified = true;
        context.Entry(estado).Property(x => x.FechaActualizacion).IsModified = true;
        await context.SaveChangesAsync();
    }

    public async Task MarcarErrorAsync(string tipoDocumento, int documentoId, string? ultimoError)
    {
        var estado = await GetOrCreateAsync(tipoDocumento, documentoId);
        estado.CorreoEnviado = false;
        estado.UltimoErrorCorreo = string.IsNullOrWhiteSpace(ultimoError) ? null : ultimoError.Trim();
        estado.FechaActualizacion = DateTime.Now;

        await using var context = await _dbFactory.CreateDbContextAsync();
        context.Attach(estado);
        context.Entry(estado).Property(x => x.CorreoEnviado).IsModified = true;
        context.Entry(estado).Property(x => x.UltimoErrorCorreo).IsModified = true;
        context.Entry(estado).Property(x => x.FechaActualizacion).IsModified = true;
        await context.SaveChangesAsync();
    }

    public async Task<List<int>> GetDocumentosPendientesAsync(string tipoDocumento, int maxRegistros)
    {
        if (string.IsNullOrWhiteSpace(tipoDocumento) || maxRegistros <= 0)
            return new List<int>();

        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Set<ComprobanteCorreoEstado>()
            .AsNoTracking()
            .Where(x => x.TipoDocumento == tipoDocumento && !x.CorreoEnviado)
            .OrderBy(x => x.FechaActualizacion)
            .ThenBy(x => x.Id)
            .Select(x => x.DocumentoId)
            .Take(maxRegistros)
            .ToListAsync();
    }

    private async Task<ComprobanteCorreoEstado> GetOrCreateAsync(string tipoDocumento, int documentoId)
    {
        if (documentoId <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentoId));

        if (string.IsNullOrWhiteSpace(tipoDocumento))
            throw new ArgumentException("El tipo de documento es obligatorio.", nameof(tipoDocumento));

        await EnsureSchemaAsync();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var estado = await context.Set<ComprobanteCorreoEstado>()
            .FirstOrDefaultAsync(x =>
                x.TipoDocumento == tipoDocumento &&
                x.DocumentoId == documentoId);

        if (estado != null)
            return estado;

        estado = new ComprobanteCorreoEstado
        {
            TipoDocumento = tipoDocumento.Trim(),
            DocumentoId = documentoId,
            CorreoEnviado = false,
            FechaRegistro = DateTime.Now,
            FechaActualizacion = DateTime.Now
        };

        context.Set<ComprobanteCorreoEstado>().Add(estado);
        await context.SaveChangesAsync();
        return estado;
    }

    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return @"
IF OBJECT_ID('dbo.COMPROBANTECORREOESTADO', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[COMPROBANTECORREOESTADO]
    (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_COMPROBANTECORREOESTADO] PRIMARY KEY,
        [TipoDocumento] NVARCHAR(40) NOT NULL,
        [DocumentoId] INT NOT NULL,
        [CorreoEnviado] BIT NOT NULL CONSTRAINT [DF_COMPROBANTECORREOESTADO_CorreoEnviado] DEFAULT(0),
        [FechaEnvioCorreo] DATETIME NULL,
        [UltimoErrorCorreo] NVARCHAR(1000) NULL,
        [FechaRegistro] DATETIME NOT NULL CONSTRAINT [DF_COMPROBANTECORREOESTADO_FechaRegistro] DEFAULT(GETDATE()),
        [FechaActualizacion] DATETIME NOT NULL CONSTRAINT [DF_COMPROBANTECORREOESTADO_FechaActualizacion] DEFAULT(GETDATE())
    );
END";

        yield return @"
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_COMPROBANTECORREOESTADO_TipoDocumento_DocumentoId'
      AND object_id = OBJECT_ID('dbo.COMPROBANTECORREOESTADO')
)
BEGIN
    CREATE UNIQUE INDEX [UX_COMPROBANTECORREOESTADO_TipoDocumento_DocumentoId]
        ON [dbo].[COMPROBANTECORREOESTADO]([TipoDocumento], [DocumentoId]);
END";
    }
}
