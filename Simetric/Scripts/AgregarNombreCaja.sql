IF COL_LENGTH('dbo.CAJA', 'nombreCaja') IS NULL
BEGIN
    ALTER TABLE [dbo].[CAJA] ADD [nombreCaja] NVARCHAR(100) NULL;
END;

UPDATE [dbo].[CAJA]
SET [nombreCaja] = CONCAT(N'Caja número ', [numCaja])
WHERE ([nombreCaja] IS NULL OR LTRIM(RTRIM([nombreCaja])) = N'')
  AND [numCaja] IS NOT NULL;
