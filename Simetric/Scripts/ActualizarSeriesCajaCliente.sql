/*
  Actualiza de forma transaccional todas las series de una caja.
  Complete los cuatro parámetros antes de ejecutar manualmente.
  La pantalla /administracion/cajas-secuencias realiza esta misma operación.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @IdUsuario INT = 0;
DECLARE @CajaSec INT = 0;
DECLARE @Establecimiento CHAR(3) = '001';
DECLARE @PuntoEmision CHAR(3) = '001';

IF @IdUsuario <= 0 OR @CajaSec <= 0
    THROW 51000, 'Debe indicar un IdUsuario y CajaSec válidos.', 1;

IF @Establecimiento NOT LIKE '[0-9][0-9][0-9]'
   OR @PuntoEmision NOT LIKE '[0-9][0-9][0-9]'
   OR @Establecimiento = '000'
   OR @PuntoEmision = '000'
    THROW 51001, 'Establecimiento y punto de emisión deben tener tres dígitos y no pueden ser 000.', 1;

DECLARE @SerieVisual VARCHAR(7) = CONCAT(@Establecimiento, '-', @PuntoEmision);
DECLARE @SerieRaw VARCHAR(6) = CONCAT(@Establecimiento, @PuntoEmision);
DECLARE @NumeroCaja INT;
DECLARE @EsCajaSistema BIT;
DECLARE @TitularId INT = @IdUsuario;
DECLARE @OldDocumentSeries TABLE
(
    DocumentKey NVARCHAR(80) NOT NULL,
    SeriesRaw VARCHAR(6) NOT NULL,
    PRIMARY KEY (DocumentKey, SeriesRaw)
);

BEGIN TRANSACTION;

SELECT
    @NumeroCaja = ISNULL(c.[numCaja], 0),
    @EsCajaSistema = CONVERT(bit, ISNULL(c.[es_caja_sistema], 0))
FROM [dbo].[CAJA] c WITH (UPDLOCK, HOLDLOCK)
WHERE c.[sec] = @CajaSec
  AND c.[idUsuario] = @IdUsuario;

IF @NumeroCaja IS NULL
    THROW 51002, 'La caja no existe o no pertenece al usuario indicado.', 1;

SELECT @TitularId =
    CASE
        WHEN ISNULL(u.[estadoAsociado], 0) = 1 AND ISNULL(u.[idJefe], 0) > 0 THEN u.[idJefe]
        ELSE u.[IdUsuario]
    END
FROM [dbo].[Usuarios] u
WHERE u.[IdUsuario] = @IdUsuario;

IF EXISTS (
    SELECT 1
    FROM [dbo].[CAJA] c
    INNER JOIN [dbo].[Usuarios] u ON u.[IdUsuario] = c.[idUsuario]
    WHERE c.[sec] <> @CajaSec
      AND c.[estado] = 1
      AND ISNULL(c.[es_caja_sistema], 0) = 0
      AND (u.[IdUsuario] = @TitularId OR (u.[idJefe] = @TitularId AND ISNULL(u.[estadoAsociado], 0) = 1))
      AND @SerieVisual IN (c.[serieFactura], c.[serieCompras], c.[serieGuia], c.[serieDebitos], c.[serieNotasCred])
)
    THROW 51003, 'La nueva serie ya está asignada a otra caja de la cuenta.', 1;

INSERT INTO @OldDocumentSeries (DocumentKey, SeriesRaw)
SELECT DISTINCT v.DocumentKey, RIGHT(REPLACE(v.Serie, '-', ''), 6)
FROM [dbo].[CAJA] c
CROSS APPLY (VALUES
    (N'factura', c.[serieFactura]),
    (N'guia-remision', c.[serieGuia]),
    (N'nota-credito', c.[serieNotasCred]),
    (N'nota-debito', c.[serieDebitos]),
    (N'liquidacion-compra', c.[serieCompras]),
    (N'compra-manual', c.[serieCompras]),
    (N'retencion', c.[serieCompras])
) v(DocumentKey, Serie)
WHERE c.[sec] = @CajaSec
  AND NULLIF(LTRIM(RTRIM(v.Serie)), '') IS NOT NULL;

DECLARE @StateChanges TABLE
(
    CajaSec INT NOT NULL,
    DocumentKey NVARCHAR(80) NOT NULL,
    SeriesKey NVARCHAR(120) NOT NULL,
    Initialized BIT NOT NULL,
    LastSequence BIGINT NOT NULL,
    PRIMARY KEY (CajaSec, DocumentKey, SeriesKey)
);

INSERT INTO @StateChanges (CajaSec, DocumentKey, SeriesKey, Initialized, LastSequence)
SELECT CajaSec, DocumentKey, SeriesKey, Initialized, LastSequence
FROM (
    SELECT
        s.[cajaSec] AS CajaSec,
        s.[documentKey] AS DocumentKey,
        CONCAT(
            CASE WHEN CHARINDEX(':', s.[seriesKey]) > 0
                 THEN LEFT(s.[seriesKey], CHARINDEX(':', s.[seriesKey]))
                 ELSE '' END,
            @SerieRaw) AS SeriesKey,
        CONVERT(bit, s.[initialized]) AS Initialized,
        CONVERT(bigint, s.[lastSequence]) AS LastSequence,
        ROW_NUMBER() OVER (
            PARTITION BY s.[cajaSec], s.[documentKey],
                CONCAT(CASE WHEN CHARINDEX(':', s.[seriesKey]) > 0
                            THEN LEFT(s.[seriesKey], CHARINDEX(':', s.[seriesKey]))
                            ELSE '' END, @SerieRaw)
            ORDER BY s.[updatedAt] DESC, s.[id] DESC
        ) AS RowNumber
    FROM [dbo].[CAJA_SECUENCIA] s
    WHERE s.[cajaSec] = @CajaSec
      AND EXISTS (
          SELECT 1
          FROM @OldDocumentSeries old
          WHERE old.DocumentKey = s.[documentKey]
            AND old.SeriesRaw = RIGHT(REPLACE(ISNULL(s.[seriesKey], ''), '-', ''), 6)
      )
) source
WHERE source.RowNumber = 1;

MERGE [dbo].[CAJA_SECUENCIA] WITH (HOLDLOCK) AS target
USING @StateChanges AS source
ON target.[cajaSec] = source.CajaSec
   AND target.[documentKey] = source.DocumentKey
   AND target.[seriesKey] = source.SeriesKey
WHEN MATCHED THEN
    UPDATE SET [initialized] = source.Initialized,
               [lastSequence] = source.LastSequence,
               [updatedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([cajaSec], [documentKey], [seriesKey], [initialized], [lastSequence])
    VALUES (source.CajaSec, source.DocumentKey, source.SeriesKey, source.Initialized, source.LastSequence);

DELETE s
FROM [dbo].[CAJA_SECUENCIA] s
WHERE s.[cajaSec] = @CajaSec
  AND EXISTS (
      SELECT 1
      FROM @OldDocumentSeries old
      WHERE old.DocumentKey = s.[documentKey]
        AND old.SeriesRaw = RIGHT(REPLACE(ISNULL(s.[seriesKey], ''), '-', ''), 6)
  )
  AND RIGHT(REPLACE(ISNULL(s.[seriesKey], ''), '-', ''), 6) <> @SerieRaw;

UPDATE [dbo].[CAJA]
SET [serieFactura] = @SerieVisual,
    [serieCompras] = @SerieVisual,
    [serieGuia] = @SerieVisual,
    [serieDebitos] = @SerieVisual,
    [serieNotasCred] = @SerieVisual
WHERE [sec] = @CajaSec
  AND [idUsuario] = @IdUsuario;

UPDATE [dbo].[CAJA_SECUENCIA_PREFERENCIA]
SET [seriesKey] = @SerieRaw,
    [updatedAt] = SYSUTCDATETIME()
WHERE [titularUserId] = @TitularId
  AND EXISTS (
      SELECT 1
      FROM @OldDocumentSeries old
      WHERE old.DocumentKey = [dbo].[CAJA_SECUENCIA_PREFERENCIA].[documentKey]
        AND old.SeriesRaw = RIGHT(REPLACE(ISNULL([dbo].[CAJA_SECUENCIA_PREFERENCIA].[seriesKey], ''), '-', ''), 6)
  );

IF @NumeroCaja = 1
BEGIN
    UPDATE em
    SET em.[codEstablecimiento] = @Establecimiento,
        em.[codPuntoEmision] = @PuntoEmision
    FROM [dbo].[EMISOR] em
    WHERE em.[ESTADO] = 1
      AND (
          (@EsCajaSistema = 1 AND ISNULL(em.[es_emisor_sistema], 0) = 1)
          OR
          (@EsCajaSistema = 0 AND em.[id_usuario] IN (
              SELECT u.[IdUsuario]
              FROM [dbo].[Usuarios] u
              WHERE u.[IdUsuario] = @TitularId
                 OR (u.[idJefe] = @TitularId AND ISNULL(u.[estadoAsociado], 0) = 1)
          ))
      );
END;

COMMIT TRANSACTION;

SELECT @CajaSec AS CajaSec, @IdUsuario AS IdUsuario, @SerieVisual AS NuevaSerie;
