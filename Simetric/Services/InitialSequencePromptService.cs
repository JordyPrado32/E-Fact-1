using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;

namespace Simetric.Services;

public sealed class InitialSequencePromptService
{
    private sealed record DocumentSequenceColumns(string InitializedColumn, string LastSequenceColumn);

    private static readonly Dictionary<string, DocumentSequenceColumns> DocumentColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["factura"] = new("secuenciaFacturaInicializada", "ultimoSecuencialFactura"),
            ["guia-remision"] = new("secuenciaGuiaInicializada", "ultimoSecuencialGuia"),
            ["nota-credito"] = new("secuenciaNotaCreditoInicializada", "ultimoSecuencialNotaCredito"),
            ["nota-debito"] = new("secuenciaNotaDebitoInicializada", "ultimoSecuencialNotaDebito"),
            ["liquidacion-compra"] = new("secuenciaLiquidacionInicializada", "ultimoSecuencialLiquidacion"),
            ["compra-manual"] = new("secuenciaCompraManualInicializada", "ultimoSecuencialCompraManual"),
            ["retencion"] = new("secuenciaRetencionInicializada", "ultimoSecuencialRetencion")
        };

    public static IReadOnlyList<InitialSequenceDocumentInfo> Documents { get; } =
        new[]
        {
            new InitialSequenceDocumentInfo("factura", "Factura"),
            new InitialSequenceDocumentInfo("guia-remision", "Guia de remision"),
            new InitialSequenceDocumentInfo("nota-credito", "Nota de credito"),
            new InitialSequenceDocumentInfo("nota-debito", "Nota de debito"),
            new InitialSequenceDocumentInfo("liquidacion-compra", "Liquidacion de compra"),
            new InitialSequenceDocumentInfo("compra-manual", "Compra manual"),
            new InitialSequenceDocumentInfo("retencion", "Retencion")
        };

    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICajaSerieResolver _cajaSerieResolver;

    public InitialSequencePromptService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICajaSerieResolver cajaSerieResolver)
    {
        _dbFactory = dbFactory;
        _cajaSerieResolver = cajaSerieResolver;
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
            {
                await context.Database.ExecuteSqlRawAsync(statement);
            }

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<InitialSequencePromptState> GetStateAsync(
    int userId,
    string documentKey,
    string? seriesKey = null,
    int? emisorId = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(documentKey))
            return new InitialSequencePromptState();

        var columns = ResolveColumns(documentKey);
        if (columns is null)
            return new InitialSequencePromptState();

        await EnsureSchemaAsync();

        var normalizedDocumentKey = NormalizeDocumentKey(documentKey);
        var hasExplicitSeries = !string.IsNullOrWhiteSpace(seriesKey);

        var series = await ResolveSeriesKeyAsync(userId, seriesKey, emisorId);
        if (string.IsNullOrWhiteSpace(series))
            return new InitialSequencePromptState();

        var legacySeries = emisorId is > 0
            ? await ResolveSeriesKeyAsync(userId, seriesKey)
            : string.Empty;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, userId, emisorId);
        var connection = context.Database.GetDbConnection();

        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
            await connection.OpenAsync();

        try
        {
            async Task<InitialSequencePromptState?> ReadStateAsync(string currentSeries)
            {
                if (string.IsNullOrWhiteSpace(currentSeries))
                    return null;

                var currentCajaSecs = await GetCajaSecsAsync(context, userId, currentSeries, emisorId, usuariosSincronizados);
                if (currentCajaSecs.Count == 0)
                    return null;

                foreach (var currentCajaSec in currentCajaSecs)
                {
                    if (string.Equals(normalizedDocumentKey, "factura", StringComparison.OrdinalIgnoreCase))
                    {
                        var facturaLegacyState = await TryReadLegacyStateAsync(
                            connection,
                            currentCajaSec,
                            normalizedDocumentKey,
                            currentSeries,
                            columns);

                        if (facturaLegacyState is not null)
                            return facturaLegacyState;
                    }

                    var sequenceState = await TryReadSequenceStateAsync(
                        connection,
                        currentCajaSec,
                        normalizedDocumentKey,
                        currentSeries);

                    if (sequenceState is not null)
                        return sequenceState;

                    var legacyState = await TryReadLegacyStateAsync(
                        connection,
                        currentCajaSec,
                        normalizedDocumentKey,
                        currentSeries,
                        columns
                        );

                    if (legacyState is not null)
                        return legacyState;
                }

                return null;
            }

            var state = await ReadStateAsync(series);
            if (state?.Initialized == true)
                return state;

            var pendingState = state;

            if (!string.IsNullOrWhiteSpace(legacySeries) &&
                !string.Equals(series, legacySeries, StringComparison.OrdinalIgnoreCase))
            {
                state = await ReadStateAsync(legacySeries);
                if (state?.Initialized == true)
                    return state;

                pendingState ??= state;
            }

            if (!hasExplicitSeries &&
                !string.Equals(normalizedDocumentKey, "factura", StringComparison.OrdinalIgnoreCase))
            {
                var initializedAccountState = await TryReadAnyInitializedAccountStateAsync(
                    context,
                    connection,
                    usuariosSincronizados,
                    normalizedDocumentKey,
                    columns);

                if (initializedAccountState is not null)
                    return initializedAccountState;
            }

            return pendingState ?? new InitialSequencePromptState();
        }
        finally
        {
            if (shouldCloseConnection)
                await connection.CloseAsync();
        }
    }

    public async Task SaveStateAsync(int userId, string documentKey, string? seriesKey, InitialSequencePromptState state, int? emisorId = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(documentKey))
            return;

        var columns = ResolveColumns(documentKey);
        if (columns is null)
            return;

        await EnsureSchemaAsync();

        var series = await ResolveSeriesKeyAsync(userId, seriesKey, emisorId);
        if (string.IsNullOrWhiteSpace(series))
            return;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, userId, emisorId);
        var cajaSecs = await GetCajaSecsAsync(context, userId, series, emisorId, usuariosSincronizados);
        if (cajaSecs.Count == 0)
            return;

        state.ConfiguredAt ??= DateTimeOffset.UtcNow;

        long lastSequence = 0;
        if (state.Initialized && state.HadPreviousDocuments)
            TryGetSequenceNumber(state.PreviousSequence, out lastSequence);

        foreach (var cajaSec in cajaSecs)
        {
            await context.Database.ExecuteSqlRawAsync(
                BuildSaveStateSql(),
                cajaSec,
                NormalizeDocumentKey(documentKey),
                series,
                state.Initialized,
                lastSequence);

            if (string.Equals(NormalizeDocumentKey(documentKey), "factura", StringComparison.OrdinalIgnoreCase))
            {
                var caja = await context.Caja.FirstOrDefaultAsync(c => c.Sec == cajaSec);
                if (caja is not null)
                {
                    caja.SecuenciaFacturaInicializada = state.Initialized;
                    caja.UltimoSecuencialFactura = lastSequence;
                }
            }
        }

        if (string.Equals(NormalizeDocumentKey(documentKey), "factura", StringComparison.OrdinalIgnoreCase))
        {
            await context.SaveChangesAsync();
        }
    }

    public async Task UpdateLastSequenceAsync(int userId, string documentKey, string? lastSequence, string? seriesKey = null, int? emisorId = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(documentKey))
            return;

        var columns = ResolveColumns(documentKey);
        if (columns is null)
            return;

        if (!TryGetSequenceNumber(lastSequence, out var normalizedLastSequence))
            return;

        await EnsureSchemaAsync();

        var series = await ResolveSeriesKeyAsync(userId, seriesKey, emisorId);
        if (string.IsNullOrWhiteSpace(series))
            return;

        await using var context = await _dbFactory.CreateDbContextAsync();
        var usuariosSincronizados = await GetUsuariosSincronizadosPorEmisorRucAsync(context, userId, emisorId);
        var cajaSecs = await GetCajaSecsAsync(context, userId, series, emisorId, usuariosSincronizados);
        if (cajaSecs.Count == 0)
            return;

        foreach (var cajaSec in cajaSecs)
        {
            await context.Database.ExecuteSqlRawAsync(
                BuildUpdateLastSequenceSql(),
                cajaSec,
                NormalizeDocumentKey(documentKey),
                series,
                true,
                normalizedLastSequence);

            if (string.Equals(NormalizeDocumentKey(documentKey), "factura", StringComparison.OrdinalIgnoreCase))
            {
                var caja = await context.Caja.FirstOrDefaultAsync(c => c.Sec == cajaSec);
                if (caja is not null)
                {
                    var ultimoActual = caja.UltimoSecuencialFactura ?? 0;
                    if (normalizedLastSequence > ultimoActual)
                    {
                        caja.SecuenciaFacturaInicializada = true;
                        caja.UltimoSecuencialFactura = normalizedLastSequence;
                    }
                }
            }
        }

        if (string.Equals(NormalizeDocumentKey(documentKey), "factura", StringComparison.OrdinalIgnoreCase))
        {
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<InitialSequenceDocumentState>> GetDocumentStatesAsync(int userId, string? seriesKey = null)
    {
        var states = new List<InitialSequenceDocumentState>();
        foreach (var document in Documents)
        {
            var state = await GetStateAsync(userId, document.Key, seriesKey);
            states.Add(new InitialSequenceDocumentState
            {
                DocumentKey = document.Key,
                Label = document.Label,
                Initialized = state.Initialized,
                LastSequence = state.PreviousSequence
            });
        }

        return states;
    }

    public async Task SaveDocumentStateAsync(int userId, string documentKey, string? seriesKey, bool initialized, string? lastSequence)
    {
        if (!Documents.Any(x => string.Equals(x.Key, documentKey, StringComparison.OrdinalIgnoreCase)))
            return;

        var normalized = string.Empty;
        if (initialized && !string.IsNullOrWhiteSpace(lastSequence))
        {
            if (!TryNormalizeSequence(lastSequence, out normalized))
                throw new InvalidOperationException("La secuencia debe estar entre 000000001 y 999999999.");
        }

        await SaveStateAsync(
            userId,
            documentKey,
            seriesKey,
            new InitialSequencePromptState
            {
                Initialized = initialized,
                HadPreviousDocuments = initialized && !string.IsNullOrWhiteSpace(normalized),
                PreviousSequence = normalized
            });
    }

    public async Task<string> GetPreferredSeriesKeyAsync(int userId, string documentKey, string? fallbackSeriesKey = null)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(documentKey))
            return NormalizeSeriesKey(fallbackSeriesKey);

        await EnsureSchemaAsync();

        var titularCuentaId = await GetTitularCuentaIdAsync(userId);
        if (titularCuentaId <= 0)
            return NormalizeSeriesKey(fallbackSeriesKey);

        await using var context = await _dbFactory.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        var shouldCloseConnection = false;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            shouldCloseConnection = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT [seriesKey]
FROM [dbo].[CAJA_SECUENCIA_PREFERENCIA]
WHERE [titularUserId] = @titularUserId
  AND [documentKey] = @documentKey";
            AddParameter(command, "@titularUserId", titularCuentaId);
            AddParameter(command, "@documentKey", NormalizeDocumentKey(documentKey));

            var result = await command.ExecuteScalarAsync();
            var preferred = NormalizeSeriesKey(Convert.ToString(result, CultureInfo.InvariantCulture));
            return string.IsNullOrWhiteSpace(preferred)
                ? NormalizeSeriesKey(fallbackSeriesKey)
                : preferred;
        }
        finally
        {
            if (shouldCloseConnection)
                await connection.CloseAsync();
        }
    }

    public async Task SavePreferredSeriesKeyAsync(int userId, string documentKey, string? seriesKey)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(documentKey))
            return;

        var normalizedSeriesKey = NormalizeSeriesKey(seriesKey);
        if (string.IsNullOrWhiteSpace(normalizedSeriesKey))
            return;

        await EnsureSchemaAsync();

        var titularCuentaId = await GetTitularCuentaIdAsync(userId);
        if (titularCuentaId <= 0)
            return;

        await using var context = await _dbFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync(
            BuildSavePreferredSeriesSql(),
            titularCuentaId,
            NormalizeDocumentKey(documentKey),
            normalizedSeriesKey);
    }

    public bool TryNormalizeSequence(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (!TryGetSequenceNumber(value, out var number))
            return false;

        normalized = number.ToString("D9", CultureInfo.InvariantCulture);
        return true;
    }

    public string GetNextSequenceFromPrevious(string? previousSequence)
    {
        if (!TryGetSequenceNumber(previousSequence, out var number))
            return string.Empty;

        return (number + 1).ToString("D9", CultureInfo.InvariantCulture);
    }

    public string ResolveNextSequence(string? automaticNext, InitialSequencePromptState state)
    {
        long nextNumber = 0;

        if (TryGetSequenceNumber(automaticNext, out var automaticNumber))
        {
            nextNumber = automaticNumber;
        }

        if (state.Initialized &&
            state.HadPreviousDocuments &&
            TryGetSequenceNumber(state.PreviousSequence, out var previousNumber))
        {
            nextNumber = Math.Max(nextNumber, previousNumber + 1);
        }

        if (nextNumber <= 0 && state.Initialized && state.HadPreviousDocuments == false)
            nextNumber = 1;

        if (nextNumber <= 0)
            return string.Empty;

        return nextNumber.ToString("D9", CultureInfo.InvariantCulture);
    }

    private async Task<List<int>> GetCajaSecsAsync(AppDbContext context, int userId, string? seriesKey = null, int? emisorId = null, List<int>? usuariosSincronizados = null)
    {
        try
        {
            if (await EsEmisorSistemaAsync(context, emisorId))
            {
                return await GetCajaSistemaSecsAsync(context, seriesKey);
            }

            usuariosSincronizados ??= await GetUsuariosSincronizadosPorEmisorRucAsync(context, userId, emisorId);
            var normalizedSeries = NormalizeSeriesLookupKey(seriesKey);
            if (!string.IsNullOrWhiteSpace(normalizedSeries))
            {
                var targetSeriesVisual = $"{normalizedSeries[..3]}-{normalizedSeries[3..]}";
                if (usuariosSincronizados.Count > 0)
                {
                    var cajasPorSerie = await context.Caja
                        .AsNoTracking()
                        .Where(c =>
                            c.Estado == true &&
                            c.IdUsuario.HasValue &&
                            usuariosSincronizados.Contains(c.IdUsuario.Value) &&
                            (c.SerieFactura == targetSeriesVisual ||
                             c.SerieNotasCred == targetSeriesVisual ||
                             c.SerieGuia == targetSeriesVisual ||
                             c.SerieDebitos == targetSeriesVisual ||
                             c.SerieCompras == targetSeriesVisual))
                        .OrderBy(c => c.NumCaja)
                        .ThenBy(c => c.Sec)
                        .Select(c => c.Sec)
                        .ToListAsync();

                    if (cajasPorSerie.Count > 0)
                        return cajasPorSerie;
                }
            }

            if (usuariosSincronizados.Count > 0)
            {
                var cajas = await context.Caja
                    .AsNoTracking()
                    .Where(c =>
                        c.Estado == true &&
                        c.IdUsuario.HasValue &&
                        usuariosSincronizados.Contains(c.IdUsuario.Value))
                    .OrderBy(c => c.NumCaja)
                    .ThenBy(c => c.Sec)
                    .Select(c => c.Sec)
                    .ToListAsync();

                if (cajas.Count > 0)
                    return cajas;
            }

            var caja = await _cajaSerieResolver.ObtenerCajaAsync(userId);
            return caja?.Sec > 0 ? new List<int> { caja.Sec } : new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    private static DocumentSequenceColumns? ResolveColumns(string documentKey)
        => DocumentColumns.TryGetValue(documentKey.Trim(), out var columns) ? columns : null;

    private static IEnumerable<string> BuildEnsureSchemaStatements()
    {
        yield return @"
IF OBJECT_ID('dbo.CAJA_SECUENCIA', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CAJA_SECUENCIA] (
        [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CAJA_SECUENCIA] PRIMARY KEY,
        [cajaSec] INT NOT NULL,
        [documentKey] NVARCHAR(50) NOT NULL,
        [seriesKey] VARCHAR(20) NOT NULL,
        [initialized] BIT NOT NULL CONSTRAINT [DF_CAJA_SECUENCIA_initialized] DEFAULT(0),
        [lastSequence] BIGINT NOT NULL CONSTRAINT [DF_CAJA_SECUENCIA_lastSequence] DEFAULT(0),
        [createdAt] DATETIME2 NOT NULL CONSTRAINT [DF_CAJA_SECUENCIA_createdAt] DEFAULT(SYSUTCDATETIME()),
        [updatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CAJA_SECUENCIA_updatedAt] DEFAULT(SYSUTCDATETIME())
    );
END";

        yield return @"
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_CAJA_SECUENCIA_CajaDocumentoSerie'
      AND object_id = OBJECT_ID('dbo.CAJA_SECUENCIA')
)
BEGIN
    CREATE UNIQUE INDEX [UX_CAJA_SECUENCIA_CajaDocumentoSerie]
    ON [dbo].[CAJA_SECUENCIA] ([cajaSec], [documentKey], [seriesKey]);
END";

        yield return @"
IF OBJECT_ID('dbo.CAJA_SECUENCIA_PREFERENCIA', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CAJA_SECUENCIA_PREFERENCIA] (
        [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_CAJA_SECUENCIA_PREFERENCIA] PRIMARY KEY,
        [titularUserId] INT NOT NULL,
        [documentKey] NVARCHAR(50) NOT NULL,
        [seriesKey] VARCHAR(20) NOT NULL,
        [updatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_CAJA_SECUENCIA_PREFERENCIA_updatedAt] DEFAULT(SYSUTCDATETIME())
    );
END";

        yield return @"
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_CAJA_SECUENCIA_PREFERENCIA_TitularDocumento'
      AND object_id = OBJECT_ID('dbo.CAJA_SECUENCIA_PREFERENCIA')
)
BEGIN
    CREATE UNIQUE INDEX [UX_CAJA_SECUENCIA_PREFERENCIA_TitularDocumento]
    ON [dbo].[CAJA_SECUENCIA_PREFERENCIA] ([titularUserId], [documentKey]);
END";

        foreach (var columns in DocumentColumns.Values)
        {
            yield return $@"
IF COL_LENGTH('dbo.CAJA', '{columns.InitializedColumn}') IS NULL
BEGIN
    ALTER TABLE [dbo].[CAJA] ADD [{columns.InitializedColumn}] BIT NULL;
END";

            yield return $@"
IF COL_LENGTH('dbo.CAJA', '{columns.LastSequenceColumn}') IS NULL
BEGIN
    ALTER TABLE [dbo].[CAJA] ADD [{columns.LastSequenceColumn}] BIGINT NULL;
END";
        }

        yield return @"
IF COL_LENGTH('dbo.CAJA', 'es_caja_sistema') IS NULL
BEGIN
    ALTER TABLE [dbo].[CAJA] ADD [es_caja_sistema] BIT NOT NULL CONSTRAINT [DF_CAJA_es_caja_sistema] DEFAULT(0);
END";
    }

    private static string BuildSaveStateSql()
        => @"
MERGE [dbo].[CAJA_SECUENCIA] AS target
USING (SELECT @p0 AS cajaSec, @p1 AS documentKey, @p2 AS seriesKey) AS source
ON target.[cajaSec] = source.cajaSec
   AND target.[documentKey] = source.documentKey
   AND target.[seriesKey] = source.seriesKey
WHEN MATCHED THEN
    UPDATE SET [initialized] = @p3,
               [lastSequence] = @p4,
               [updatedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([cajaSec], [documentKey], [seriesKey], [initialized], [lastSequence])
    VALUES (@p0, @p1, @p2, @p3, @p4);";

    private static string BuildUpdateLastSequenceSql()
        => @"
MERGE [dbo].[CAJA_SECUENCIA] AS target
USING (SELECT @p0 AS cajaSec, @p1 AS documentKey, @p2 AS seriesKey) AS source
ON target.[cajaSec] = source.cajaSec
   AND target.[documentKey] = source.documentKey
   AND target.[seriesKey] = source.seriesKey
WHEN MATCHED THEN
    UPDATE SET [initialized] = @p3,
               [lastSequence] = CASE WHEN ISNULL(target.[lastSequence], 0) < @p4 THEN @p4 ELSE target.[lastSequence] END,
               [updatedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([cajaSec], [documentKey], [seriesKey], [initialized], [lastSequence])
     VALUES (@p0, @p1, @p2, @p3, @p4);";

    private static string BuildSavePreferredSeriesSql()
        => @"
MERGE [dbo].[CAJA_SECUENCIA_PREFERENCIA] AS target
USING (SELECT @p0 AS titularUserId, @p1 AS documentKey) AS source
ON target.[titularUserId] = source.titularUserId
   AND target.[documentKey] = source.documentKey
WHEN MATCHED THEN
    UPDATE SET [seriesKey] = @p2,
               [updatedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([titularUserId], [documentKey], [seriesKey])
    VALUES (@p0, @p1, @p2);";

    private async Task<string> ResolveSeriesKeyAsync(int userId, string? seriesKey, int? emisorId = null)
    {
        var normalized = NormalizeSeriesKey(seriesKey);
        if (!string.IsNullOrWhiteSpace(normalized))
            return BuildScopedSeriesKey(normalized, emisorId);

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            if (await EsEmisorSistemaAsync(context, emisorId))
            {
                var serieSistema = await ResolveCajaSistemaSerieAsync(context);
                return BuildScopedSeriesKey(serieSistema, emisorId);
            }

            var resolucion = await _cajaSerieResolver.ResolverAsync(userId);
            return BuildScopedSeriesKey(NormalizeSeriesKey(resolucion.SerieRaw), emisorId);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<InitialSequencePromptState?> TryReadSequenceStateAsync(
        DbConnection connection,
        int cajaSec,
        string documentKey,
        string seriesKey)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT [initialized], [lastSequence]
FROM [dbo].[CAJA_SECUENCIA]
WHERE [cajaSec] = @sec
  AND [documentKey] = @documentKey
  AND [seriesKey] = @seriesKey";

        AddParameter(command, "@sec", cajaSec);
        AddParameter(command, "@documentKey", documentKey);
        AddParameter(command, "@seriesKey", seriesKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var initialized = !reader.IsDBNull(0) && Convert.ToBoolean(reader.GetValue(0));
        var lastSequence = !reader.IsDBNull(1) ? Convert.ToInt64(reader.GetValue(1)) : 0;

        return new InitialSequencePromptState
        {
            Initialized = initialized,
            HadPreviousDocuments = initialized && lastSequence > 0,
            PreviousSequence = lastSequence > 0
                ? lastSequence.ToString("D9", CultureInfo.InvariantCulture)
                : string.Empty
        };
    }

    private static async Task<InitialSequencePromptState?> TryReadAnyInitializedAccountStateAsync(
        AppDbContext context,
        DbConnection connection,
        List<int> usuariosCuenta,
        string documentKey,
        DocumentSequenceColumns columns)
    {
        if (usuariosCuenta.Count == 0)
            return null;

        var cajaSecs = await context.Caja
            .AsNoTracking()
            .Where(c =>
                c.Estado == true &&
                c.IdUsuario.HasValue &&
                usuariosCuenta.Contains(c.IdUsuario.Value))
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .Select(c => c.Sec)
            .ToListAsync();

        foreach (var cajaSec in cajaSecs)
        {
            await using var sequenceCommand = connection.CreateCommand();
            sequenceCommand.CommandText = @"
SELECT TOP 1 [initialized], [lastSequence]
FROM [dbo].[CAJA_SECUENCIA]
WHERE [cajaSec] = @sec
  AND [documentKey] = @documentKey
  AND [initialized] = 1
ORDER BY [lastSequence] DESC";
            AddParameter(sequenceCommand, "@sec", cajaSec);
            AddParameter(sequenceCommand, "@documentKey", documentKey);

            await using var sequenceReader = await sequenceCommand.ExecuteReaderAsync();
            if (await sequenceReader.ReadAsync())
            {
                var lastSequence = !sequenceReader.IsDBNull(1) ? Convert.ToInt64(sequenceReader.GetValue(1)) : 0;
                return new InitialSequencePromptState
                {
                    Initialized = true,
                    HadPreviousDocuments = lastSequence > 0,
                    PreviousSequence = lastSequence > 0
                        ? lastSequence.ToString("D9", CultureInfo.InvariantCulture)
                        : string.Empty
                };
            }
            await sequenceReader.CloseAsync();

            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.CommandText = $@"
SELECT [{columns.InitializedColumn}], [{columns.LastSequenceColumn}]
FROM [dbo].[CAJA]
WHERE [sec] = @sec";
            AddParameter(legacyCommand, "@sec", cajaSec);

            await using var legacyReader = await legacyCommand.ExecuteReaderAsync();
            if (!await legacyReader.ReadAsync())
                continue;

            var initialized = !legacyReader.IsDBNull(0) && Convert.ToBoolean(legacyReader.GetValue(0));
            var legacyLastSequence = !legacyReader.IsDBNull(1) ? Convert.ToInt64(legacyReader.GetValue(1)) : 0;

            if (initialized)
            {
                return new InitialSequencePromptState
                {
                    Initialized = true,
                    HadPreviousDocuments = legacyLastSequence > 0,
                    PreviousSequence = legacyLastSequence > 0
                        ? legacyLastSequence.ToString("D9", CultureInfo.InvariantCulture)
                        : string.Empty
                };
            }
        }

        return null;
    }

    private async Task<InitialSequencePromptState?> TryReadLegacyStateAsync(
        DbConnection connection,
        int cajaSec,
        string documentKey,
        string seriesKey,
        DocumentSequenceColumns columns)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = @"
SELECT COUNT(1)
FROM [dbo].[CAJA_SECUENCIA]
WHERE [cajaSec] = @sec
  AND [documentKey] = @documentKey";
        AddParameter(countCommand, "@sec", cajaSec);
        AddParameter(countCommand, "@documentKey", documentKey);

        var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        if (existingCount > 0)
            return null;

        await using var legacyCommand = connection.CreateCommand();
        legacyCommand.CommandText = $@"
SELECT [{columns.InitializedColumn}], [{columns.LastSequenceColumn}]
FROM [dbo].[CAJA]
WHERE [sec] = @sec";
        AddParameter(legacyCommand, "@sec", cajaSec);

        await using var reader = await legacyCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var initialized = !reader.IsDBNull(0) && Convert.ToBoolean(reader.GetValue(0));
        var lastSequence = !reader.IsDBNull(1) ? Convert.ToInt64(reader.GetValue(1)) : 0;
        await reader.CloseAsync();

        if (!initialized && lastSequence <= 0)
            return null;

        await using var saveCommand = connection.CreateCommand();
        saveCommand.CommandText = @"
INSERT INTO [dbo].[CAJA_SECUENCIA] ([cajaSec], [documentKey], [seriesKey], [initialized], [lastSequence])
SELECT @sec, @documentKey, @seriesKey, @initialized, @lastSequence
WHERE NOT EXISTS (
    SELECT 1
    FROM [dbo].[CAJA_SECUENCIA]
    WHERE [cajaSec] = @sec
      AND [documentKey] = @documentKey
      AND [seriesKey] = @seriesKey
)";
        AddParameter(saveCommand, "@sec", cajaSec);
        AddParameter(saveCommand, "@documentKey", documentKey);
        AddParameter(saveCommand, "@seriesKey", seriesKey);
        AddParameter(saveCommand, "@initialized", initialized);
        AddParameter(saveCommand, "@lastSequence", lastSequence);
        await saveCommand.ExecuteNonQueryAsync();

        return new InitialSequencePromptState
        {
            Initialized = initialized,
            HadPreviousDocuments = initialized && lastSequence > 0,
            PreviousSequence = lastSequence > 0
                ? lastSequence.ToString("D9", CultureInfo.InvariantCulture)
                : string.Empty
        };
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool TryGetSequenceNumber(string? value, out long number)
    {
        number = 0;

        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits) || digits.Length > 9)
            return false;

        return long.TryParse(digits, out number) && number is >= 1 and <= 999999999;
    }

    private static string NormalizeDocumentKey(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeSeriesKey(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 6 ? digits[..6] : string.Empty;
    }

    private static string NormalizeSeriesLookupKey(string? value)
    {
        var rawValue = value ?? string.Empty;
        var scopeSeparatorIndex = rawValue.LastIndexOf(':');
        if (scopeSeparatorIndex >= 0)
            rawValue = rawValue[(scopeSeparatorIndex + 1)..];

        return NormalizeSeriesKey(rawValue);
    }

    private static string BuildScopedSeriesKey(string seriesKey, int? emisorId)
    {
        if (string.IsNullOrWhiteSpace(seriesKey))
            return string.Empty;

        return emisorId is > 0
            ? $"E{emisorId.Value}:{seriesKey}"
            : seriesKey;
    }

    private static async Task<bool> EsEmisorSistemaAsync(AppDbContext context, int? emisorId)
    {
        if (emisorId is not > 0)
        {
            return false;
        }

        return await context.Emisores
            .AsNoTracking()
            .AnyAsync(e => e.Codigo == emisorId.Value && e.Estado && e.EsEmisorSistema);
    }

    private static async Task<List<int>> GetCajaSistemaSecsAsync(AppDbContext context, string? seriesKey = null)
    {
        var normalizedSeries = NormalizeSeriesLookupKey(seriesKey);
        var query = context.Caja
            .AsNoTracking()
            .Where(c => c.Estado == true && c.EsCajaSistema == true);

        if (!string.IsNullOrWhiteSpace(normalizedSeries))
        {
            var targetSeriesVisual = $"{normalizedSeries[..3]}-{normalizedSeries[3..]}";
            query = query.Where(c =>
                c.SerieFactura == targetSeriesVisual ||
                c.SerieNotasCred == targetSeriesVisual ||
                c.SerieGuia == targetSeriesVisual ||
                c.SerieDebitos == targetSeriesVisual ||
                c.SerieCompras == targetSeriesVisual);
        }

        return await query
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .Select(c => c.Sec)
            .ToListAsync();
    }

    private static async Task<string> ResolveCajaSistemaSerieAsync(AppDbContext context)
    {
        var serie = await context.Caja
            .AsNoTracking()
            .Where(c => c.Estado == true && c.EsCajaSistema == true && c.SerieFactura != null)
            .OrderBy(c => c.NumCaja)
            .ThenBy(c => c.Sec)
            .Select(c => c.SerieFactura)
            .FirstOrDefaultAsync();

        return NormalizeSeriesKey(serie);
    }

    private static async Task<int> GetTitularCuentaIdAsync(AppDbContext context, int idUsuario)
    {
        if (idUsuario <= 0)
            return 0;

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

        if (usuario == null)
            return 0;

        return usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : usuario.IdUsuario;
    }

    private static async Task<List<int>> GetUsuariosCuentaIdsAsync(AppDbContext context, int idUsuario)
    {
        var titularId = await GetTitularCuentaIdAsync(context, idUsuario);
        if (titularId <= 0)
            return new List<int>();

        return await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();
    }

    private static async Task<List<int>> GetUsuariosSincronizadosPorEmisorRucAsync(AppDbContext context, int idUsuario, int? emisorId = null)
    {
        var usuarios = await GetUsuariosCuentaIdsAsync(context, idUsuario);
        if (!usuarios.Contains(idUsuario))
        {
            usuarios.Add(idUsuario);
        }

        IQueryable<Emisor> query = context.Emisores
            .AsNoTracking()
            .Where(e =>
                e.Estado &&
                !e.EsEmisorSistema &&
                e.IdUsuario.HasValue &&
                e.Ruc != null &&
                e.Ruc != string.Empty);

        if (emisorId is > 0)
        {
            query = query.Where(e => e.Codigo == emisorId.Value || usuarios.Contains(e.IdUsuario.Value));
        }
        else
        {
            query = query.Where(e => usuarios.Contains(e.IdUsuario.Value));
        }

        var rucs = await query
            .Select(e => e.Ruc!.Trim())
            .Distinct()
            .ToListAsync();

        if (rucs.Count == 0)
        {
            return usuarios.Distinct().ToList();
        }

        var usuariosPorRuc = await context.Emisores
            .AsNoTracking()
            .Where(e =>
                e.Estado &&
                !e.EsEmisorSistema &&
                e.IdUsuario.HasValue &&
                e.Ruc != null &&
                rucs.Contains(e.Ruc.Trim()))
            .Select(e => e.IdUsuario!.Value)
            .Distinct()
            .ToListAsync();

        return usuarios
            .Concat(usuariosPorRuc)
            .Distinct()
            .ToList();
    }

    private async Task<int> GetTitularCuentaIdAsync(int idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await GetTitularCuentaIdAsync(context, idUsuario);
    }
}

public sealed class InitialSequencePromptState
{
    public bool Initialized { get; set; }
    public bool HadPreviousDocuments { get; set; }
    public string PreviousSequence { get; set; } = string.Empty;
    public DateTimeOffset? ConfiguredAt { get; set; }
}

public sealed record InitialSequenceDocumentInfo(string Key, string Label);

public sealed class InitialSequenceDocumentState
{
    public string DocumentKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Initialized { get; set; }
    public string LastSequence { get; set; } = string.Empty;
    public string SelectedSeriesRaw { get; set; } = string.Empty;
    public string SelectedSeriesVisual { get; set; } = string.Empty;
}
