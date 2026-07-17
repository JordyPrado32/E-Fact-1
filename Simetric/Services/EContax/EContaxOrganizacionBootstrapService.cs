using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services.EContax;

public sealed class EContaxOrganizacionBootstrapService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public EContaxOrganizacionBootstrapService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task EnsureSchemaAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        await context.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'[dbo].[EMPRESA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[EMPRESA] (
        [idEmpresa] INT NOT NULL,
        [nombre] NVARCHAR(200) NOT NULL,
        [ruc] NVARCHAR(20) NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_EMPRESA_estado] DEFAULT (1),
        [fechaCreacion] DATETIME2 NOT NULL CONSTRAINT [DF_EMPRESA_fechaCreacion] DEFAULT (SYSUTCDATETIME()),
        [fechaActualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_EMPRESA] PRIMARY KEY ([idEmpresa])
    );
END;

IF OBJECT_ID(N'[dbo].[SUCURSAL]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SUCURSAL] (
        [idSucursal] INT NOT NULL,
        [idEmpresa] INT NOT NULL,
        [nombre] NVARCHAR(200) NOT NULL,
        [codigo] NVARCHAR(20) NULL,
        [direccion] NVARCHAR(300) NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_SUCURSAL_estado] DEFAULT (1),
        [fechaCreacion] DATETIME2 NOT NULL CONSTRAINT [DF_SUCURSAL_fechaCreacion] DEFAULT (SYSUTCDATETIME()),
        [fechaActualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_SUCURSAL] PRIMARY KEY ([idEmpresa], [idSucursal]),
        CONSTRAINT [FK_SUCURSAL_EMPRESA] FOREIGN KEY ([idEmpresa]) REFERENCES [dbo].[EMPRESA]([idEmpresa])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_USUARIO_CONTEXTO]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] (
        [id_usuario] INT NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_estado] DEFAULT (1),
        [fecha_creacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_fecha_creacion] DEFAULT (SYSUTCDATETIME()),
        [fecha_actualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_ECONTAX_USUARIO_CONTEXTO] PRIMARY KEY ([id_usuario]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_USUARIO] FOREIGN KEY ([id_usuario]) REFERENCES [dbo].[Usuarios]([IdUsuario]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_EMPRESA] FOREIGN KEY ([id_empresa]) REFERENCES [dbo].[EMPRESA]([idEmpresa]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL] FOREIGN KEY ([id_empresa], [id_sucursal]) REFERENCES [dbo].[SUCURSAL]([idEmpresa], [idSucursal])
    );
END;

IF COL_LENGTH('dbo.CLIENTES', 'IDEMPRESA') IS NULL
    ALTER TABLE [dbo].[CLIENTES] ADD [IDEMPRESA] INT NULL;

IF COL_LENGTH('dbo.CLIENTES', 'IDSUCURSAL') IS NULL
    ALTER TABLE [dbo].[CLIENTES] ADD [IDSUCURSAL] INT NULL;

IF COL_LENGTH('dbo.CLIENTES', 'DIAS_CREDITO') IS NULL
    ALTER TABLE [dbo].[CLIENTES] ADD [DIAS_CREDITO] INT NULL;

IF COL_LENGTH('dbo.PRODUCTO', 'IDEMPRESA') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [IDEMPRESA] INT NULL;

IF COL_LENGTH('dbo.PRODUCTO', 'IDSUCURSAL') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [IDSUCURSAL] INT NULL;

IF OBJECT_ID(N'[dbo].[ECONTAX_USUARIO_ROL]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_USUARIO_ROL] (
        [id_usuario] INT NOT NULL,
        [id_rol] INT NOT NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_ROL_estado] DEFAULT (1),
        [fecha_creacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_ROL_fecha_creacion] DEFAULT (SYSUTCDATETIME()),
        [fecha_actualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_ECONTAX_USUARIO_ROL] PRIMARY KEY ([id_usuario])
    );
END;

IF OBJECT_ID(N'[dbo].[SUCURSAL]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL]', N'F') IS NOT NULL
        ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] DROP CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL];

    IF OBJECT_ID(N'[dbo].[PK_SUCURSAL]', N'PK') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1
           FROM sys.key_constraints kc
           INNER JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
           INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
           WHERE kc.parent_object_id = OBJECT_ID(N'[dbo].[SUCURSAL]')
             AND kc.[name] = N'PK_SUCURSAL'
           GROUP BY kc.object_id
           HAVING COUNT(*) = 2
              AND SUM(CASE WHEN c.[name] IN (N'idEmpresa', N'idSucursal') THEN 1 ELSE 0 END) = 2
       )
        ALTER TABLE [dbo].[SUCURSAL] DROP CONSTRAINT [PK_SUCURSAL];

    IF OBJECT_ID(N'[dbo].[PK_SUCURSAL]', N'PK') IS NULL
        ALTER TABLE [dbo].[SUCURSAL] ADD CONSTRAINT [PK_SUCURSAL] PRIMARY KEY ([idEmpresa], [idSucursal]);
END;
""");

        await context.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID(N'[dbo].[rol]', N'U') IS NOT NULL
BEGIN
    DECLARE @nextRol INT = ISNULL((SELECT MAX([id_rol]) FROM [dbo].[rol]), 0);

    IF NOT EXISTS (SELECT 1 FROM [dbo].[rol] WHERE UPPER(LTRIM(RTRIM([nombre_rol]))) = 'JEFE_EMPRESA')
    BEGIN
        SET @nextRol = @nextRol + 1;
        INSERT INTO [dbo].[rol] ([id_rol], [nombre_rol], [permiso_rol], [estado_rol])
        VALUES (@nextRol, 'JEFE_EMPRESA', 1001, 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[rol] WHERE UPPER(LTRIM(RTRIM([nombre_rol]))) = 'ADMIN_SUCURSAL')
    BEGIN
        SET @nextRol = @nextRol + 1;
        INSERT INTO [dbo].[rol] ([id_rol], [nombre_rol], [permiso_rol], [estado_rol])
        VALUES (@nextRol, 'ADMIN_SUCURSAL', 1002, 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[rol] WHERE UPPER(LTRIM(RTRIM([nombre_rol]))) = 'USUARIO_SUCURSAL')
    BEGIN
        SET @nextRol = @nextRol + 1;
        INSERT INTO [dbo].[rol] ([id_rol], [nombre_rol], [permiso_rol], [estado_rol])
        VALUES (@nextRol, 'USUARIO_SUCURSAL', 1003, 1);
    END;
END;

INSERT INTO [dbo].[EMPRESA] ([idEmpresa], [nombre], [ruc], [estado])
SELECT DISTINCT
    e.[idEmpresa],
    COALESCE(NULLIF(LTRIM(RTRIM(MAX(e.[razonSocial]))), ''), CONCAT('Empresa ', e.[idEmpresa])),
    NULLIF(LTRIM(RTRIM(MAX(e.[RUC]))), ''),
    1
FROM [dbo].[EMISOR] e
WHERE e.[idEmpresa] IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[EMPRESA] emp WHERE emp.[idEmpresa] = e.[idEmpresa]
  )
GROUP BY e.[idEmpresa];

INSERT INTO [dbo].[EMPRESA] ([idEmpresa], [nombre], [estado])
SELECT DISTINCT
    c.[idEmpresa],
    CONCAT('Empresa ', c.[idEmpresa]),
    1
FROM [dbo].[CAJA] c
WHERE c.[idEmpresa] IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[EMPRESA] emp WHERE emp.[idEmpresa] = c.[idEmpresa]
  );

IF NOT EXISTS (SELECT 1 FROM [dbo].[EMPRESA])
BEGIN
    INSERT INTO [dbo].[EMPRESA] ([idEmpresa], [nombre], [estado])
    VALUES (1, 'Empresa principal', 1);
END;

INSERT INTO [dbo].[SUCURSAL] ([idSucursal], [idEmpresa], [nombre], [codigo], [direccion], [estado])
SELECT DISTINCT
    e.[idSucursal],
    e.[idEmpresa],
    COALESCE(NULLIF(LTRIM(RTRIM(MAX(e.[dirEstablecimiento]))), ''), CONCAT('Sucursal ', e.[idSucursal])),
    NULLIF(LTRIM(RTRIM(MAX(e.[codEstablecimiento]))), ''),
    NULLIF(LTRIM(RTRIM(MAX(e.[DIRECCION]))), ''),
    1
FROM [dbo].[EMISOR] e
WHERE e.[idEmpresa] IS NOT NULL
  AND e.[idSucursal] IS NOT NULL
  AND EXISTS (SELECT 1 FROM [dbo].[EMPRESA] emp WHERE emp.[idEmpresa] = e.[idEmpresa])
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[SUCURSAL] suc WHERE suc.[idEmpresa] = e.[idEmpresa] AND suc.[idSucursal] = e.[idSucursal]
  )
GROUP BY e.[idEmpresa], e.[idSucursal];

INSERT INTO [dbo].[SUCURSAL] ([idSucursal], [idEmpresa], [nombre], [codigo], [estado])
SELECT
    c.[idSucursal],
    c.[idEmpresa],
    CONCAT('Sucursal ', c.[idSucursal]),
    TRY_CONVERT(NVARCHAR(20), MAX(c.[numCaja])),
    1
FROM [dbo].[CAJA] c
WHERE c.[idEmpresa] IS NOT NULL
  AND c.[idSucursal] IS NOT NULL
  AND EXISTS (SELECT 1 FROM [dbo].[EMPRESA] emp WHERE emp.[idEmpresa] = c.[idEmpresa])
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[SUCURSAL] suc WHERE suc.[idEmpresa] = c.[idEmpresa] AND suc.[idSucursal] = c.[idSucursal]
  )
GROUP BY c.[idEmpresa], c.[idSucursal];

IF NOT EXISTS (SELECT 1 FROM [dbo].[SUCURSAL])
BEGIN
    INSERT INTO [dbo].[SUCURSAL] ([idSucursal], [idEmpresa], [nombre], [codigo], [estado])
    SELECT TOP (1) 1, [idEmpresa], 'Matriz', '001', 1
    FROM [dbo].[EMPRESA]
    ORDER BY [idEmpresa];
END;

INSERT INTO [dbo].[ECONTAX_USUARIO_CONTEXTO] ([id_usuario], [id_empresa], [id_sucursal], [estado])
SELECT
    u.[IdUsuario],
    COALESCE(c.[idEmpresa], e.[idEmpresa], (SELECT TOP (1) [idEmpresa] FROM [dbo].[EMPRESA] ORDER BY [idEmpresa])),
    COALESCE(c.[idSucursal], e.[idSucursal], (SELECT TOP (1) [idSucursal] FROM [dbo].[SUCURSAL] ORDER BY [idSucursal])),
    1
FROM [dbo].[Usuarios] u
OUTER APPLY (
    SELECT TOP (1) ca.[idEmpresa], ca.[idSucursal]
    FROM [dbo].[CAJA] ca
    WHERE ca.[idUsuario] = u.[IdUsuario]
      AND ISNULL(ca.[estado], 0) = 1
      AND ca.[idEmpresa] IS NOT NULL
    ORDER BY ca.[sec]
) c
OUTER APPLY (
    SELECT TOP (1) em.[idEmpresa], em.[idSucursal]
    FROM [dbo].[EMISOR] em
    WHERE em.[id_usuario] = COALESCE(u.[idJefe], u.[IdUsuario])
      AND ISNULL(em.[ESTADO], 0) = 1
      AND em.[idEmpresa] IS NOT NULL
    ORDER BY em.[codigo]
) e
WHERE ISNULL(u.[Estado], 0) = 1
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[ECONTAX_USUARIO_CONTEXTO] uc WHERE uc.[id_usuario] = u.[IdUsuario]
  );

;WITH ConsumidoresFinales AS (
    SELECT
        c.[CODCLIENTE],
        c.[IDEMPRESA],
        ROW_NUMBER() OVER (
            PARTITION BY c.[IDEMPRESA]
            ORDER BY
                CASE WHEN ISNULL(c.[ESTADO], 0) = 1 THEN 0 ELSE 1 END,
                c.[CODCLIENTE]
        ) AS rn
    FROM [dbo].[CLIENTES] c
    WHERE c.[IDEMPRESA] IS NOT NULL
      AND LTRIM(RTRIM(ISNULL(c.[NUMEROIDENTIFICACION], ''))) = '9999999999999'
)
UPDATE c
SET c.[APELLIDOS] = 'Final',
    c.[NOMBRES] = 'Consumidor',
    c.[NOMBRECOMERCIAL] = NULL,
    c.[NOMBRERAZONSOCIAL] = NULL,
    c.[DIRECCION] = COALESCE(NULLIF(LTRIM(RTRIM(c.[DIRECCION])), ''), 'Consumidor Final'),
    c.[TELEFONOCONVENCIONAL] = COALESCE(NULLIF(LTRIM(RTRIM(c.[TELEFONOCONVENCIONAL])), ''), '022222222'),
    c.[CELULAR] = COALESCE(NULLIF(LTRIM(RTRIM(c.[CELULAR])), ''), '0999999999'),
    c.[CORREO] = 'consumidorfinal@numerica',
    c.[OBLGCONTA] = 'NO',
    c.[IDSUCURSAL] = NULL,
    c.[ESTADO] = CASE WHEN cf.[rn] = 1 THEN 1 ELSE 0 END
FROM [dbo].[CLIENTES] c
INNER JOIN ConsumidoresFinales cf ON cf.[CODCLIENTE] = c.[CODCLIENTE];

UPDATE p
SET p.[IDEMPRESA] = uc.[id_empresa],
    p.[IDSUCURSAL] = COALESCE(p.[IDSUCURSAL], uc.[id_sucursal])
FROM [dbo].[PRODUCTO] p
INNER JOIN [dbo].[Usuarios] u ON u.[IdUsuario] = p.[IDUSUARIO]
INNER JOIN [dbo].[ECONTAX_USUARIO_CONTEXTO] uc ON uc.[id_usuario] = COALESCE(u.[idJefe], u.[IdUsuario])
WHERE p.[IDEMPRESA] IS NULL OR p.[IDSUCURSAL] IS NULL;

IF OBJECT_ID(N'[dbo].[FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL]', N'F') IS NULL
   AND OBJECT_ID(N'[dbo].[ECONTAX_USUARIO_CONTEXTO]', N'U') IS NOT NULL
   AND OBJECT_ID(N'[dbo].[SUCURSAL]', N'U') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] WITH CHECK ADD CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL]
    FOREIGN KEY ([id_empresa], [id_sucursal]) REFERENCES [dbo].[SUCURSAL]([idEmpresa], [idSucursal]);

    ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] CHECK CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL];
END;
""");

        await context.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CLIENTES_ECONTAX_EMPRESA_IDENTIFICACION' AND object_id = OBJECT_ID('dbo.CLIENTES'))
   AND NOT EXISTS (
       SELECT 1
       FROM [dbo].[CLIENTES]
       WHERE [IDEMPRESA] IS NOT NULL
         AND [NUMEROIDENTIFICACION] IS NOT NULL
         AND [ESTADO] = 1
       GROUP BY [IDEMPRESA], [NUMEROIDENTIFICACION]
       HAVING COUNT(*) > 1
   )
BEGIN
    CREATE UNIQUE INDEX [IX_CLIENTES_ECONTAX_EMPRESA_IDENTIFICACION]
    ON [dbo].[CLIENTES] ([IDEMPRESA], [NUMEROIDENTIFICACION])
    WHERE [IDEMPRESA] IS NOT NULL
      AND [NUMEROIDENTIFICACION] IS NOT NULL
      AND [ESTADO] = 1;
END;
""");

        await context.Database.ExecuteSqlRawAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PRODUCTO_ECONTAX_SUCURSAL_CODIGO' AND object_id = OBJECT_ID('dbo.PRODUCTO'))
   AND NOT EXISTS (
       SELECT 1
       FROM [dbo].[PRODUCTO]
       WHERE [IDEMPRESA] IS NOT NULL
         AND [IDSUCURSAL] IS NOT NULL
         AND [CODIGO_PRINCIPAL] IS NOT NULL
         AND [ESTADO] = 1
       GROUP BY [IDEMPRESA], [IDSUCURSAL], [CODIGO_PRINCIPAL]
       HAVING COUNT(*) > 1
   )
BEGIN
    CREATE UNIQUE INDEX [IX_PRODUCTO_ECONTAX_SUCURSAL_CODIGO]
    ON [dbo].[PRODUCTO] ([IDEMPRESA], [IDSUCURSAL], [CODIGO_PRINCIPAL])
    WHERE [IDEMPRESA] IS NOT NULL
      AND [IDSUCURSAL] IS NOT NULL
      AND [CODIGO_PRINCIPAL] IS NOT NULL
      AND [ESTADO] = 1;
END;
""");
    }
}
