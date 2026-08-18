using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Simetric.Services;

public sealed class AdminCajaSecuenciaService
{
    public const string Route = "/administracion/cajas-secuencias";

    private static readonly IReadOnlyDictionary<string, string> DocumentLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["factura"] = "Factura",
            ["guia-remision"] = "Guía de remisión",
            ["nota-credito"] = "Nota de crédito",
            ["nota-debito"] = "Nota de débito",
            ["liquidacion-compra"] = "Liquidación de compra",
            ["compra-manual"] = "Compra manual",
            ["retencion"] = "Retención"
        };

    private readonly string _connectionString;
    private readonly AuditService _auditService;

    public AdminCajaSecuenciaService(IConfiguration configuration, AuditService auditService)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("La cadena de conexión no existe.");
        _auditService = auditService;
    }

    public async Task EnsureMenuAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = """
DECLARE @administracionId INT;
DECLARE @menuId INT;
DECLARE @administracionCreada BIT = 0;

SELECT TOP (1) @administracionId = [IDMENU]
FROM [dbo].[MENUS]
WHERE ISNULL([IDMENUPADRE], 0) = 0
  AND [NOMBREMENU] COLLATE Latin1_General_CI_AI = N'Administracion'
ORDER BY [IDMENU];

IF @administracionId IS NULL
BEGIN
    INSERT INTO [dbo].[MENUS]
        ([IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU], [orden_menu])
    VALUES
        (NULL, N'Administración', 1, NULL, N'ri-settings-3-line', 90);

    SET @administracionId = CONVERT(INT, SCOPE_IDENTITY());
    SET @administracionCreada = 1;
END;

SELECT TOP (1) @menuId = [IDMENU]
FROM [dbo].[MENUS]
WHERE [RUTAMENU] = @route;

IF @menuId IS NULL
BEGIN
    INSERT INTO [dbo].[MENUS]
        ([IDMENUPADRE], [NOMBREMENU], [ESTADOMENU], [RUTAMENU], [ICONOMENU], [orden_menu])
    VALUES
        (@administracionId, N'Cajas y secuencias', 1, @route, N'ri-safe-2-line', 30);

    SET @menuId = CONVERT(INT, SCOPE_IDENTITY());

    INSERT INTO [dbo].[ROL_MENU] ([IDROL], [IDMENU])
    SELECT r.[IDROL], @menuId
    FROM [dbo].[ROLES] r
    WHERE r.[ESTADOROL] = 1
      AND r.[IDTIPOUSUARIO] = 2
      AND NOT EXISTS (
          SELECT 1
          FROM [dbo].[ROL_MENU] rm
          WHERE rm.[IDROL] = r.[IDROL]
            AND rm.[IDMENU] = @menuId
      );
END;

IF @administracionCreada = 1
BEGIN
    INSERT INTO [dbo].[ROL_MENU] ([IDROL], [IDMENU])
    SELECT r.[IDROL], @administracionId
    FROM [dbo].[ROLES] r
    WHERE r.[ESTADOROL] = 1
      AND r.[IDTIPOUSUARIO] = 2
      AND NOT EXISTS (
          SELECT 1
          FROM [dbo].[ROL_MENU] rm
          WHERE rm.[IDROL] = r.[IDROL]
            AND rm.[IDMENU] = @administracionId
      );
END;
""";

        await connection.ExecuteAsync(sql, new { route = Route });
    }

    public async Task<List<AdminCajaSecuenciaDto>> GetAllAsync(int actorUserId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await EnsurePermissionAsync(connection, actorUserId);

        const string sql = """
SELECT
    c.[sec] AS CajaSec,
    ISNULL(c.[numCaja], 0) AS NumeroCaja,
    ISNULL(c.[idUsuario], 0) AS IdUsuario,
    CONCAT(LTRIM(RTRIM(ISNULL(u.[Nombres], ''))), ' ', LTRIM(RTRIM(ISNULL(u.[Apellidos], '')))) AS Cliente,
    ISNULL(u.[Email], '') AS Email,
    ISNULL(e.[RUC], '') AS Ruc,
    ISNULL(e.[razonSocial], ISNULL(u.[NombreEmpresa], '')) AS RazonSocial,
    CAST(ISNULL(c.[estado], 0) AS bit) AS Activa,
    CAST(ISNULL(c.[es_caja_sistema], 0) AS bit) AS EsCajaSistema,
    ISNULL(doc.SerieVisual, '') AS Serie,
    doc.DocumentKey,
    doc.DocumentLabel,
    doc.DocumentOrder,
    CAST(COALESCE(sequenceState.[initialized], doc.LegacyInitialized, 0) AS bit) AS Initialized,
    CONVERT(BIGINT, COALESCE(sequenceState.[lastSequence], doc.LegacyLastSequence, 0)) AS LastSequence
FROM [dbo].[CAJA] c
INNER JOIN [dbo].[Usuarios] u ON u.[IdUsuario] = c.[idUsuario]
OUTER APPLY (
    SELECT TOP (1) em.[RUC], em.[razonSocial]
    FROM [dbo].[EMISOR] em
    WHERE em.[id_usuario] = c.[idUsuario]
      AND em.[ESTADO] = 1
    ORDER BY em.[es_emisor_sistema], em.[codigo]
) e
CROSS APPLY (VALUES
    (N'factura', N'Factura', 1, c.[serieFactura],
        CONVERT(bit, c.[secuenciaFacturaInicializada]), CONVERT(bigint, c.[ultimoSecuencialFactura])),
    (N'guia-remision', N'Guía de remisión', 2, c.[serieGuia],
        CONVERT(bit, c.[secuenciaGuiaInicializada]), CONVERT(bigint, c.[ultimoSecuencialGuia])),
    (N'nota-credito', N'Nota de crédito', 3, c.[serieNotasCred],
        CONVERT(bit, c.[secuenciaNotaCreditoInicializada]), CONVERT(bigint, c.[ultimoSecuencialNotaCredito])),
    (N'nota-debito', N'Nota de débito', 4, c.[serieDebitos],
        CONVERT(bit, c.[secuenciaNotaDebitoInicializada]), CONVERT(bigint, c.[ultimoSecuencialNotaDebito])),
    (N'liquidacion-compra', N'Liquidación de compra', 5, c.[serieCompras],
        CONVERT(bit, c.[secuenciaLiquidacionInicializada]), CONVERT(bigint, c.[ultimoSecuencialLiquidacion])),
    (N'compra-manual', N'Compra manual', 6, c.[serieCompras],
        CONVERT(bit, c.[secuenciaCompraManualInicializada]), CONVERT(bigint, c.[ultimoSecuencialCompraManual])),
    (N'retencion', N'Retención', 7, c.[serieCompras],
        CONVERT(bit, c.[secuenciaRetencionInicializada]), CONVERT(bigint, c.[ultimoSecuencialRetencion]))
) doc(DocumentKey, DocumentLabel, DocumentOrder, SerieVisual, LegacyInitialized, LegacyLastSequence)
OUTER APPLY (
    SELECT TOP (1) s.[initialized], s.[lastSequence]
    FROM [dbo].[CAJA_SECUENCIA] s
    WHERE s.[cajaSec] = c.[sec]
      AND s.[documentKey] = doc.DocumentKey
      AND RIGHT(s.[seriesKey], 6) = RIGHT(REPLACE(ISNULL(doc.SerieVisual, ''), '-', ''), 6)
    ORDER BY s.[updatedAt] DESC, s.[id] DESC
) sequenceState
ORDER BY Cliente, c.[numCaja], c.[sec], doc.DocumentOrder;
""";

        var rows = (await connection.QueryAsync<AdminCajaSecuenciaRow>(sql)).ToList();

        return rows
            .GroupBy(row => new
            {
                row.CajaSec,
                row.NumeroCaja,
                row.IdUsuario,
                row.Cliente,
                row.Email,
                row.Ruc,
                row.RazonSocial,
                row.Activa,
                row.EsCajaSistema
            })
            .Select(group => new AdminCajaSecuenciaDto
            {
                CajaSec = group.Key.CajaSec,
                NumeroCaja = group.Key.NumeroCaja,
                IdUsuario = group.Key.IdUsuario,
                Cliente = group.Key.Cliente.Trim(),
                Email = group.Key.Email,
                Ruc = group.Key.Ruc,
                RazonSocial = group.Key.RazonSocial,
                Activa = group.Key.Activa,
                EsCajaSistema = group.Key.EsCajaSistema,
                Serie = group.First().Serie,
                Secuencias = group
                    .OrderBy(row => row.DocumentOrder)
                    .Select(row => new AdminDocumentoSecuenciaDto
                    {
                        DocumentKey = row.DocumentKey,
                        DocumentLabel = row.DocumentLabel,
                        Initialized = row.Initialized,
                        LastSequence = row.LastSequence
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task SaveAsync(int actorUserId, AdminCajaSecuenciaDto model)
    {
        Validate(model);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            await EnsurePermissionAsync(connection, actorUserId, transaction);

            const string snapshotSql = """
SELECT TOP (1)
    c.[sec] AS CajaSec,
    c.[numCaja] AS NumeroCaja,
    c.[idUsuario] AS IdUsuario,
    ISNULL(c.[serieFactura], '') AS SerieFactura,
    ISNULL(c.[serieCompras], '') AS SerieCompras,
    ISNULL(c.[serieGuia], '') AS SerieGuia,
    ISNULL(c.[serieDebitos], '') AS SerieDebitos,
    ISNULL(c.[serieNotasCred], '') AS SerieNotasCred,
    CAST(ISNULL(c.[es_caja_sistema], 0) AS bit) AS EsCajaSistema
FROM [dbo].[CAJA] c WITH (UPDLOCK, HOLDLOCK)
WHERE c.[sec] = @cajaSec;
""";

            var previous = await connection.QuerySingleOrDefaultAsync<AdminCajaSnapshot>(
                snapshotSql,
                new { cajaSec = model.CajaSec },
                transaction);

            if (previous is null)
            {
                throw new InvalidOperationException("La caja seleccionada ya no existe.");
            }

            const string duplicateSql = """
DECLARE @titularId INT;

SELECT @titularId =
    CASE
        WHEN ISNULL([estadoAsociado], 0) = 1 AND ISNULL([idJefe], 0) > 0 THEN [idJefe]
        ELSE [IdUsuario]
    END
FROM [dbo].[Usuarios]
WHERE [IdUsuario] = @idUsuario;

SELECT COUNT(1)
FROM [dbo].[CAJA] c
INNER JOIN [dbo].[Usuarios] u ON u.[IdUsuario] = c.[idUsuario]
WHERE c.[sec] <> @cajaSec
  AND c.[estado] = 1
  AND ISNULL(c.[es_caja_sistema], 0) = 0
  AND (
      u.[IdUsuario] = @titularId
      OR (u.[idJefe] = @titularId AND u.[estadoAsociado] = 1)
  )
  AND @serie IN (c.[serieFactura], c.[serieCompras], c.[serieGuia], c.[serieDebitos], c.[serieNotasCred]);
""";

            var duplicates = await connection.ExecuteScalarAsync<int>(
                duplicateSql,
                new
                {
                    previous.IdUsuario,
                    model.CajaSec,
                    model.Serie
                },
                transaction);

            if (!previous.EsCajaSistema && duplicates > 0)
            {
                throw new InvalidOperationException("La serie ya está asignada a otra caja de la misma cuenta.");
            }

            var sequenceByKey = model.Secuencias.ToDictionary(item => item.DocumentKey, StringComparer.OrdinalIgnoreCase);

            const string updateCajaSql = """
UPDATE [dbo].[CAJA]
SET [serieFactura] = @serie,
    [serieCompras] = @serie,
    [serieGuia] = @serie,
    [serieDebitos] = @serie,
    [serieNotasCred] = @serie,
    [secuenciaFacturaInicializada] = @facturaInitialized,
    [ultimoSecuencialFactura] = @facturaLast,
    [secuenciaGuiaInicializada] = @guiaInitialized,
    [ultimoSecuencialGuia] = @guiaLast,
    [secuenciaNotaCreditoInicializada] = @notaCreditoInitialized,
    [ultimoSecuencialNotaCredito] = @notaCreditoLast,
    [secuenciaNotaDebitoInicializada] = @notaDebitoInitialized,
    [ultimoSecuencialNotaDebito] = @notaDebitoLast,
    [secuenciaLiquidacionInicializada] = @liquidacionInitialized,
    [ultimoSecuencialLiquidacion] = @liquidacionLast,
    [secuenciaCompraManualInicializada] = @compraInitialized,
    [ultimoSecuencialCompraManual] = @compraLast,
    [secuenciaRetencionInicializada] = @retencionInitialized,
    [ultimoSecuencialRetencion] = @retencionLast
WHERE [sec] = @cajaSec;
""";

            var affected = await connection.ExecuteAsync(
                updateCajaSql,
                new
                {
                    serie = model.Serie,
                    cajaSec = model.CajaSec,
                    facturaInitialized = sequenceByKey["factura"].Initialized,
                    facturaLast = sequenceByKey["factura"].LastSequence,
                    guiaInitialized = sequenceByKey["guia-remision"].Initialized,
                    guiaLast = sequenceByKey["guia-remision"].LastSequence,
                    notaCreditoInitialized = sequenceByKey["nota-credito"].Initialized,
                    notaCreditoLast = sequenceByKey["nota-credito"].LastSequence,
                    notaDebitoInitialized = sequenceByKey["nota-debito"].Initialized,
                    notaDebitoLast = sequenceByKey["nota-debito"].LastSequence,
                    liquidacionInitialized = sequenceByKey["liquidacion-compra"].Initialized,
                    liquidacionLast = sequenceByKey["liquidacion-compra"].LastSequence,
                    compraInitialized = sequenceByKey["compra-manual"].Initialized,
                    compraLast = sequenceByKey["compra-manual"].LastSequence,
                    retencionInitialized = sequenceByKey["retencion"].Initialized,
                    retencionLast = sequenceByKey["retencion"].LastSequence
                },
                transaction);

            if (affected != 1)
            {
                throw new InvalidOperationException("No se pudo actualizar la caja seleccionada.");
            }

            const string synchronizeSeriesSql = """
DECLARE @newSeriesRaw VARCHAR(6) = RIGHT(REPLACE(ISNULL(@newSeries, ''), '-', ''), 6);
DECLARE @titularId INT = @idUsuario;

SELECT @titularId =
    CASE
        WHEN ISNULL([estadoAsociado], 0) = 1 AND ISNULL([idJefe], 0) > 0 THEN [idJefe]
        ELSE [IdUsuario]
    END
FROM [dbo].[Usuarios]
WHERE [IdUsuario] = @idUsuario;

UPDATE [dbo].[CAJA_SECUENCIA_PREFERENCIA]
SET [seriesKey] = @newSeriesRaw,
    [updatedAt] = SYSUTCDATETIME()
WHERE [titularUserId] = @titularId
  AND (
      ([documentKey] = N'factura' AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSerieFactura, ''), '-', ''), 6))
      OR ([documentKey] = N'guia-remision' AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSerieGuia, ''), '-', ''), 6))
      OR ([documentKey] = N'nota-credito' AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSerieNotasCred, ''), '-', ''), 6))
      OR ([documentKey] = N'nota-debito' AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSerieDebitos, ''), '-', ''), 6))
      OR ([documentKey] IN (N'liquidacion-compra', N'compra-manual', N'retencion') AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSerieCompras, ''), '-', ''), 6))
  );

IF @numeroCaja = 1
BEGIN
    UPDATE em
    SET em.[codEstablecimiento] = LEFT(@newSeriesRaw, 3),
        em.[codPuntoEmision] = RIGHT(@newSeriesRaw, 3)
    FROM [dbo].[EMISOR] em
    WHERE em.[ESTADO] = 1
      AND (
          (@esCajaSistema = 1 AND ISNULL(em.[es_emisor_sistema], 0) = 1)
          OR
          (@esCajaSistema = 0 AND em.[id_usuario] IN (
              SELECT u.[IdUsuario]
              FROM [dbo].[Usuarios] u
              WHERE u.[IdUsuario] = @titularId
                 OR (u.[idJefe] = @titularId AND ISNULL(u.[estadoAsociado], 0) = 1)
          ))
      );
END;
""";

            await connection.ExecuteAsync(
                synchronizeSeriesSql,
                new
                {
                    oldSeries = previous.SerieFactura,
                    newSeries = model.Serie,
                    previous.IdUsuario,
                    previous.NumeroCaja,
                    previous.EsCajaSistema,
                    oldSerieFactura = previous.SerieFactura,
                    oldSerieCompras = previous.SerieCompras,
                    oldSerieGuia = previous.SerieGuia,
                    oldSerieDebitos = previous.SerieDebitos,
                    oldSerieNotasCred = previous.SerieNotasCred
                },
                transaction);

            const string mergeSequenceSql = """
DECLARE @oldSeriesRaw VARCHAR(6) = RIGHT(REPLACE(ISNULL(@oldSeries, ''), '-', ''), 6);

MERGE [dbo].[CAJA_SECUENCIA] WITH (HOLDLOCK) AS target
USING (
    SELECT DISTINCT
        @cajaSec AS cajaSec,
        @documentKey AS documentKey,
        scoped.SeriesKey AS seriesKey
    FROM (
        SELECT @seriesKey AS SeriesKey

        UNION ALL

        SELECT CONCAT(
            CASE
                WHEN CHARINDEX(':', existingState.[seriesKey]) > 0
                    THEN LEFT(existingState.[seriesKey], CHARINDEX(':', existingState.[seriesKey]))
                ELSE ''
            END,
            @seriesKey)
        FROM [dbo].[CAJA_SECUENCIA] existingState
        WHERE existingState.[cajaSec] = @cajaSec
          AND existingState.[documentKey] = @documentKey
          AND RIGHT(existingState.[seriesKey], 6) IN (@oldSeriesRaw, @seriesKey)

        UNION ALL

        SELECT CONCAT('E', em.[codigo], ':', @seriesKey)
        FROM [dbo].[EMISOR] em
        WHERE em.[id_usuario] = @idUsuario
          AND em.[ESTADO] = 1
    ) scoped
) AS source
ON target.[cajaSec] = source.cajaSec
   AND target.[documentKey] = source.documentKey
   AND target.[seriesKey] = source.seriesKey
WHEN MATCHED THEN
    UPDATE SET [initialized] = @initialized,
               [lastSequence] = @lastSequence,
               [updatedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([cajaSec], [documentKey], [seriesKey], [initialized], [lastSequence])
    VALUES (@cajaSec, @documentKey, @seriesKey, @initialized, @lastSequence);
""";

            var rawSeries = model.Serie.Replace("-", string.Empty, StringComparison.Ordinal);
            foreach (var sequence in model.Secuencias)
            {
                var oldSeries = GetPreviousSeries(previous, sequence.DocumentKey);
                var parameters = new
                {
                    cajaSec = model.CajaSec,
                    previous.IdUsuario,
                    oldSeries,
                    documentKey = sequence.DocumentKey,
                    seriesKey = rawSeries,
                    initialized = sequence.Initialized,
                    lastSequence = sequence.LastSequence
                };

                try
                {
                    await connection.ExecuteAsync(mergeSequenceSql, parameters, transaction);
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    const string recoverDuplicateSql = """
UPDATE [dbo].[CAJA_SECUENCIA]
SET [initialized] = @initialized,
    [lastSequence] = @lastSequence,
    [updatedAt] = SYSUTCDATETIME()
WHERE [cajaSec] = @cajaSec
  AND [documentKey] = @documentKey
  AND [seriesKey] = @seriesKey;
""";
                    var updated = await connection.ExecuteAsync(recoverDuplicateSql, parameters, transaction);
                    if (updated == 0)
                        throw;
                }

                if (!string.Equals(
                        NormalizeSeriesKey(oldSeries),
                        rawSeries,
                        StringComparison.Ordinal))
                {
                    const string deleteStaleSequenceSql = """
DELETE FROM [dbo].[CAJA_SECUENCIA]
WHERE [cajaSec] = @cajaSec
  AND [documentKey] = @documentKey
  AND RIGHT(REPLACE(ISNULL([seriesKey], ''), '-', ''), 6) = RIGHT(REPLACE(ISNULL(@oldSeries, ''), '-', ''), 6);
""";
                    await connection.ExecuteAsync(deleteStaleSequenceSql, parameters, transaction);
                }
            }

            await transaction.CommitAsync();

            await _auditService.TryRegistrarAuditoriaAsync(
                actorUserId,
                "ADMIN_CAJA_SECUENCIA_ACTUALIZADA",
                previous,
                new
                {
                    model.CajaSec,
                    model.IdUsuario,
                    model.Serie,
                    Secuencias = model.Secuencias.Select(item => new
                    {
                        item.DocumentKey,
                        item.Initialized,
                        item.LastSequence
                    })
                },
                new
                {
                    Entidad = "CajaSecuencia",
                    Tabla = "CAJA / CAJA_SECUENCIA",
                    Llaves = new { model.CajaSec }
                });
        }
        catch
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    private static async Task EnsurePermissionAsync(
        SqlConnection connection,
        int actorUserId,
        SqlTransaction? transaction = null)
    {
        const string sql = """
SELECT COUNT(1)
FROM [dbo].[Usuarios] u
INNER JOIN [dbo].[ROLES] r
    ON r.[IDTIPOUSUARIO] = u.[IdTipoUsuario]
   AND r.[ESTADOROL] = 1
INNER JOIN [dbo].[ROL_MENU] rm ON rm.[IDROL] = r.[IDROL]
INNER JOIN [dbo].[MENUS] m
    ON m.[IDMENU] = rm.[IDMENU]
   AND m.[ESTADOMENU] = 1
WHERE u.[IdUsuario] = @actorUserId
  AND u.[Estado] = 1
  AND m.[RUTAMENU] = @route;
""";

        var hasPermission = await connection.ExecuteScalarAsync<int>(
            sql,
            new { actorUserId, route = Route },
            transaction);

        if (hasPermission <= 0)
        {
            throw new UnauthorizedAccessException("No tiene permiso para administrar cajas y secuencias.");
        }
    }

    private static void Validate(AdminCajaSecuenciaDto model)
    {
        if (model.CajaSec <= 0)
        {
            throw new InvalidOperationException("La caja seleccionada no es válida.");
        }

        model.Serie = model.Serie?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(model.Serie, @"^\d{3}-\d{3}$") ||
            model.Serie.StartsWith("000-", StringComparison.Ordinal) ||
            model.Serie.EndsWith("-000", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("La serie debe tener el formato 001-001 y no puede contener 000.");
        }

        if (model.Secuencias.Count != DocumentLabels.Count ||
            model.Secuencias.Select(item => item.DocumentKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != DocumentLabels.Count ||
            model.Secuencias.Any(item => !DocumentLabels.ContainsKey(item.DocumentKey)))
        {
            throw new InvalidOperationException("Debe enviar una secuencia válida para cada tipo de documento.");
        }

        if (model.Secuencias.Any(item => item.LastSequence is < 0 or > 999_999_999))
        {
            throw new InvalidOperationException("Los secuenciales deben estar entre 0 y 999999999.");
        }
    }

    private static string GetPreviousSeries(AdminCajaSnapshot snapshot, string documentKey) =>
        documentKey switch
        {
            "factura" => snapshot.SerieFactura,
            "guia-remision" => snapshot.SerieGuia,
            "nota-credito" => snapshot.SerieNotasCred,
            "nota-debito" => snapshot.SerieDebitos,
            "liquidacion-compra" or "compra-manual" or "retencion" => snapshot.SerieCompras,
            _ => snapshot.SerieFactura
        };

    private static string NormalizeSeriesKey(string? series) =>
        new((series ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());

    private sealed class AdminCajaSecuenciaRow
    {
        public int CajaSec { get; set; }
        public int NumeroCaja { get; set; }
        public int IdUsuario { get; set; }
        public string Cliente { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Ruc { get; set; } = string.Empty;
        public string RazonSocial { get; set; } = string.Empty;
        public bool Activa { get; set; }
        public bool EsCajaSistema { get; set; }
        public string Serie { get; set; } = string.Empty;
        public string DocumentKey { get; set; } = string.Empty;
        public string DocumentLabel { get; set; } = string.Empty;
        public int DocumentOrder { get; set; }
        public bool Initialized { get; set; }
        public long LastSequence { get; set; }
    }

    private sealed class AdminCajaSnapshot
    {
        public int CajaSec { get; set; }
        public int NumeroCaja { get; set; }
        public int IdUsuario { get; set; }
        public string SerieFactura { get; set; } = string.Empty;
        public string SerieCompras { get; set; } = string.Empty;
        public string SerieGuia { get; set; } = string.Empty;
        public string SerieDebitos { get; set; } = string.Empty;
        public string SerieNotasCred { get; set; } = string.Empty;
        public bool EsCajaSistema { get; set; }
    }
}

public sealed class AdminCajaSecuenciaDto
{
    public int CajaSec { get; set; }
    public int NumeroCaja { get; set; }
    public int IdUsuario { get; set; }
    public string Cliente { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public bool Activa { get; set; }
    public bool EsCajaSistema { get; set; }
    public string Serie { get; set; } = string.Empty;
    public List<AdminDocumentoSecuenciaDto> Secuencias { get; set; } = new();
}

public sealed class AdminDocumentoSecuenciaDto
{
    public string DocumentKey { get; set; } = string.Empty;
    public string DocumentLabel { get; set; } = string.Empty;
    public bool Initialized { get; set; }
    public long LastSequence { get; set; }
}
