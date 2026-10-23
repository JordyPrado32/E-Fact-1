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
        [IdTitular] INT NULL,
        [MenusJson] NVARCHAR(MAX) NULL,
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
        [MenusJson] NVARCHAR(MAX) NULL,
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

IF OBJECT_ID(N'[dbo].[ECONTAX_BODEGA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_BODEGA] (
        [id_bodega] INT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [nombre] NVARCHAR(200) NOT NULL,
        [codigo] NVARCHAR(30) NOT NULL,
        [direccion] NVARCHAR(300) NULL,
        [observacion] NVARCHAR(500) NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_ECONTAX_BODEGA_ESTADO] DEFAULT (1),
        [fecha_creacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_BODEGA_FECHA_CREACION] DEFAULT (SYSUTCDATETIME()),
        [fecha_actualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_ECONTAX_BODEGA] PRIMARY KEY ([id_bodega]),
        CONSTRAINT [FK_ECONTAX_BODEGA_SUCURSAL] FOREIGN KEY ([id_empresa], [id_sucursal])
            REFERENCES [dbo].[SUCURSAL]([idEmpresa], [idSucursal]),
        CONSTRAINT [UX_ECONTAX_BODEGA_SUCURSAL_NOMBRE] UNIQUE ([id_empresa], [id_sucursal], [nombre]),
        CONSTRAINT [UX_ECONTAX_BODEGA_SUCURSAL_CODIGO] UNIQUE ([id_empresa], [id_sucursal], [codigo])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_USUARIO_CONTEXTO]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] (
        [id_usuario] INT NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NULL,
        [IdPerfil] INT NULL,
        [EsAdminSucursal] BIT NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_EsAdminSucursal] DEFAULT (0),
        [MenusJson] NVARCHAR(MAX) NULL,
        [estado] BIT NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_estado] DEFAULT (1),
        [fecha_creacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_fecha_creacion] DEFAULT (SYSUTCDATETIME()),
        [fecha_actualizacion] DATETIME2 NULL,
        CONSTRAINT [PK_ECONTAX_USUARIO_CONTEXTO] PRIMARY KEY ([id_usuario]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_USUARIO] FOREIGN KEY ([id_usuario]) REFERENCES [dbo].[Usuarios]([IdUsuario]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_EMPRESA] FOREIGN KEY ([id_empresa]) REFERENCES [dbo].[EMPRESA]([idEmpresa]),
        CONSTRAINT [FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL] FOREIGN KEY ([id_empresa], [id_sucursal]) REFERENCES [dbo].[SUCURSAL]([idEmpresa], [idSucursal])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_CONFIGURACION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_CONFIGURACION] (
        [id_empresa] INT NOT NULL,
        [metodo_valoracion] VARCHAR(20) NOT NULL CONSTRAINT [DF_ECONTAX_INV_CONFIG_METODO] DEFAULT ('PROMEDIO'),
        [id_usuario_actualizacion] INT NULL,
        [fecha_actualizacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_INV_CONFIG_FECHA] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ECONTAX_INVENTARIO_CONFIGURACION] PRIMARY KEY ([id_empresa])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_EXISTENCIA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_EXISTENCIA] (
        [codigo_producto] INT NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [stock_actual] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_STOCK] DEFAULT (0),
        [stock_minimo] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_MIN] DEFAULT (0),
        [stock_maximo] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_MAX] DEFAULT (0),
        [costo_promedio] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_COSTO] DEFAULT (0),
        [fecha_actualizacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_FECHA] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ECONTAX_INVENTARIO_EXISTENCIA] PRIMARY KEY ([codigo_producto], [id_empresa], [id_sucursal])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_LOTE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_LOTE] (
        [id_lote] BIGINT IDENTITY(1,1) NOT NULL,
        [codigo_producto] INT NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [cantidad_original] DECIMAL(18,4) NOT NULL,
        [cantidad_disponible] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        [fecha_ingreso] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_INV_LOTE_FECHA] DEFAULT (SYSUTCDATETIME()),
        [id_movimiento_entrada] BIGINT NULL,
        CONSTRAINT [PK_ECONTAX_INVENTARIO_LOTE] PRIMARY KEY ([id_lote])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_MOVIMIENTO]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO] (
        [id_movimiento] BIGINT IDENTITY(1,1) NOT NULL,
        [codigo_producto] INT NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [tipo_movimiento] VARCHAR(20) NOT NULL,
        [cantidad] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        [costo_total] DECIMAL(18,4) NOT NULL,
        [saldo_cantidad] DECIMAL(18,4) NOT NULL,
        [saldo_costo] DECIMAL(18,4) NOT NULL,
        [metodo_valoracion] VARCHAR(20) NOT NULL,
        [referencia_tipo] VARCHAR(30) NULL,
        [referencia] VARCHAR(100) NULL,
        [id_usuario] INT NOT NULL,
        [fecha_movimiento] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_INV_MOV_FECHA] DEFAULT (SYSUTCDATETIME()),
        [observacion] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ECONTAX_INVENTARIO_MOVIMIENTO] PRIMARY KEY ([id_movimiento])
    );
    CREATE INDEX [IX_ECONTAX_INV_MOV_PRODUCTO] ON [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO] ([id_empresa], [id_sucursal], [codigo_producto], [fecha_movimiento]);
END;

IF COL_LENGTH('dbo.ECONTAX_INVENTARIO_EXISTENCIA', 'stock_reservado') IS NULL
    ALTER TABLE [dbo].[ECONTAX_INVENTARIO_EXISTENCIA] ADD [stock_reservado] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_INV_EXIST_RESERVADO] DEFAULT (0);
IF COL_LENGTH('dbo.ECONTAX_INVENTARIO_LOTE', 'numero_lote') IS NULL
    ALTER TABLE [dbo].[ECONTAX_INVENTARIO_LOTE] ADD [numero_lote] VARCHAR(80) NULL;
IF COL_LENGTH('dbo.ECONTAX_INVENTARIO_LOTE', 'fecha_vencimiento') IS NULL
    ALTER TABLE [dbo].[ECONTAX_INVENTARIO_LOTE] ADD [fecha_vencimiento] DATE NULL;
IF COL_LENGTH('dbo.ECONTAX_INVENTARIO_MOVIMIENTO', 'numero_lote') IS NULL
    ALTER TABLE [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO] ADD [numero_lote] VARCHAR(80) NULL;
IF COL_LENGTH('dbo.ECONTAX_INVENTARIO_MOVIMIENTO', 'fecha_vencimiento') IS NULL
    ALTER TABLE [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO] ADD [fecha_vencimiento] DATE NULL;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_MOVIMIENTO_LOTE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO_LOTE] (
        [id_movimiento_lote] BIGINT IDENTITY(1,1) NOT NULL,
        [id_movimiento] BIGINT NOT NULL,
        [id_lote] BIGINT NOT NULL,
        [cantidad] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        CONSTRAINT [PK_ECONTAX_INV_MOV_LOTE] PRIMARY KEY ([id_movimiento_lote]),
        CONSTRAINT [UX_ECONTAX_INV_MOV_LOTE] UNIQUE ([id_movimiento], [id_lote]),
        CONSTRAINT [FK_ECONTAX_INV_MOV_LOTE_MOV] FOREIGN KEY ([id_movimiento]) REFERENCES [dbo].[ECONTAX_INVENTARIO_MOVIMIENTO]([id_movimiento]),
        CONSTRAINT [FK_ECONTAX_INV_MOV_LOTE_LOTE] FOREIGN KEY ([id_lote]) REFERENCES [dbo].[ECONTAX_INVENTARIO_LOTE]([id_lote])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_TRANSFERENCIA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_TRANSFERENCIA] (
        [id_transferencia] INT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [numero_transferencia] VARCHAR(30) NOT NULL,
        [id_sucursal_origen] INT NOT NULL,
        [id_sucursal_destino] INT NOT NULL,
        [estado] VARCHAR(20) NOT NULL,
        [fecha_transferencia] DATETIME2 NOT NULL,
        [id_usuario] INT NOT NULL,
        [observacion] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ECONTAX_INV_TRANSFERENCIA] PRIMARY KEY ([id_transferencia]),
        CONSTRAINT [UX_ECONTAX_INV_TRANSFERENCIA] UNIQUE ([id_empresa], [numero_transferencia])
    );
END;
IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_TRANSFERENCIA_DET]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_TRANSFERENCIA_DET] (
        [id_detalle] INT IDENTITY(1,1) NOT NULL,
        [id_transferencia] INT NOT NULL,
        [codigo_producto] INT NOT NULL,
        [cantidad] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        CONSTRAINT [PK_ECONTAX_INV_TRANSFERENCIA_DET] PRIMARY KEY ([id_detalle]),
        CONSTRAINT [FK_ECONTAX_INV_TRANSFERENCIA_DET] FOREIGN KEY ([id_transferencia]) REFERENCES [dbo].[ECONTAX_INVENTARIO_TRANSFERENCIA]([id_transferencia])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_DEVOLUCION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_DEVOLUCION] (
        [id_devolucion] INT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [id_recepcion] INT NOT NULL,
        [numero_devolucion] VARCHAR(30) NOT NULL,
        [estado] VARCHAR(20) NOT NULL,
        [fecha_devolucion] DATETIME2 NOT NULL,
        [id_usuario] INT NOT NULL,
        [observacion] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ECONTAX_INV_DEVOLUCION] PRIMARY KEY ([id_devolucion]),
        CONSTRAINT [UX_ECONTAX_INV_DEVOLUCION] UNIQUE ([id_empresa], [numero_devolucion])
    );
END;
IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_DEVOLUCION_DET]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_DEVOLUCION_DET] (
        [id_detalle] INT IDENTITY(1,1) NOT NULL,
        [id_devolucion] INT NOT NULL,
        [codigo_producto] INT NOT NULL,
        [cantidad] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        CONSTRAINT [PK_ECONTAX_INV_DEVOLUCION_DET] PRIMARY KEY ([id_detalle]),
        CONSTRAINT [FK_ECONTAX_INV_DEVOLUCION_DET] FOREIGN KEY ([id_devolucion]) REFERENCES [dbo].[ECONTAX_INVENTARIO_DEVOLUCION]([id_devolucion])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_TOMA_FISICA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_TOMA_FISICA] (
        [id_toma] INT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [id_sucursal] INT NOT NULL,
        [numero_toma] VARCHAR(30) NOT NULL,
        [estado] VARCHAR(20) NOT NULL,
        [fecha_toma] DATETIME2 NOT NULL,
        [id_usuario] INT NOT NULL,
        [observacion] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ECONTAX_INV_TOMA_FISICA] PRIMARY KEY ([id_toma]),
        CONSTRAINT [UX_ECONTAX_INV_TOMA_FISICA] UNIQUE ([id_empresa], [numero_toma])
    );
END;
IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_TOMA_FISICA_DET]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_TOMA_FISICA_DET] (
        [id_detalle] INT IDENTITY(1,1) NOT NULL,
        [id_toma] INT NOT NULL,
        [codigo_producto] INT NOT NULL,
        [stock_sistema] DECIMAL(18,4) NOT NULL,
        [stock_contado] DECIMAL(18,4) NOT NULL,
        [diferencia] DECIMAL(18,4) NOT NULL,
        [costo_unitario] DECIMAL(18,4) NOT NULL,
        CONSTRAINT [PK_ECONTAX_INV_TOMA_FISICA_DET] PRIMARY KEY ([id_detalle]),
        CONSTRAINT [FK_ECONTAX_INV_TOMA_FISICA_DET] FOREIGN KEY ([id_toma]) REFERENCES [dbo].[ECONTAX_INVENTARIO_TOMA_FISICA]([id_toma])
    );
END;

IF OBJECT_ID(N'[dbo].[ECONTAX_INVENTARIO_VALORACION_HIST]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_INVENTARIO_VALORACION_HIST] (
        [id_historial] BIGINT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [metodo_anterior] VARCHAR(20) NOT NULL,
        [metodo_nuevo] VARCHAR(20) NOT NULL,
        [id_usuario] INT NOT NULL,
        [fecha] DATETIME2 NOT NULL,
        [observacion] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ECONTAX_INV_VALORACION_HIST] PRIMARY KEY ([id_historial])
    );
END;
IF OBJECT_ID(N'[dbo].[ECONTAX_PRODUCTO_PROVEEDOR]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ECONTAX_PRODUCTO_PROVEEDOR] (
        [id_producto_proveedor] INT IDENTITY(1,1) NOT NULL,
        [id_empresa] INT NOT NULL,
        [codigo_producto] INT NOT NULL,
        [id_proveedor_cliente] INT NOT NULL,
        [codigo_producto_proveedor] VARCHAR(100) NULL,
        [precio_compra] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ECONTAX_PROD_PROV_PRECIO] DEFAULT (0),
        [es_principal] BIT NOT NULL CONSTRAINT [DF_ECONTAX_PROD_PROV_PRINCIPAL] DEFAULT (0),
        [activo] BIT NOT NULL CONSTRAINT [DF_ECONTAX_PROD_PROV_ACTIVO] DEFAULT (1),
        [fecha_actualizacion] DATETIME2 NOT NULL CONSTRAINT [DF_ECONTAX_PROD_PROV_FECHA] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ECONTAX_PRODUCTO_PROVEEDOR] PRIMARY KEY ([id_producto_proveedor]),
        CONSTRAINT [UX_ECONTAX_PRODUCTO_PROVEEDOR] UNIQUE ([id_empresa], [codigo_producto], [id_proveedor_cliente])
    );
    CREATE INDEX [IX_ECONTAX_PRODUCTO_PROVEEDOR_PROVEEDOR]
        ON [dbo].[ECONTAX_PRODUCTO_PROVEEDOR] ([id_empresa], [id_proveedor_cliente], [activo]);
END;
IF OBJECT_ID(N'[dbo].[ECONTAX_ORDEN_COMPRA]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_ORDEN_COMPRA', 'numero_factura_proveedor') IS NULL
    ALTER TABLE [dbo].[ECONTAX_ORDEN_COMPRA] ADD [numero_factura_proveedor] VARCHAR(50) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_ORDEN_COMPRA]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_ORDEN_COMPRA', 'clave_acceso_proveedor') IS NULL
    ALTER TABLE [dbo].[ECONTAX_ORDEN_COMPRA] ADD [clave_acceso_proveedor] VARCHAR(60) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_RECEPCION_COMPRA_DETALLE', 'numero_lote') IS NULL
    ALTER TABLE [dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE] ADD [numero_lote] VARCHAR(80) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_RECEPCION_COMPRA_DETALLE', 'fecha_vencimiento') IS NULL
    ALTER TABLE [dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE] ADD [fecha_vencimiento] DATE NULL;

IF COL_LENGTH('dbo.EMPRESA', 'IdTitular') IS NULL
    ALTER TABLE [dbo].[EMPRESA] ADD [IdTitular] INT NULL;
IF COL_LENGTH('dbo.EMPRESA', 'MenusJson') IS NULL
    ALTER TABLE [dbo].[EMPRESA] ADD [MenusJson] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.SUCURSAL', 'MenusJson') IS NULL
    ALTER TABLE [dbo].[SUCURSAL] ADD [MenusJson] NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'IdPerfil') IS NULL
    ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] ADD [IdPerfil] INT NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'EsAdminSucursal') IS NULL
    ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] ADD [EsAdminSucursal] BIT NOT NULL CONSTRAINT [DF_ECONTAX_USUARIO_CONTEXTO_EsAdminSucursal] DEFAULT (0);
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'MenusJson') IS NULL
    ALTER TABLE [dbo].[ECONTAX_USUARIO_CONTEXTO] ADD [MenusJson] NVARCHAR(MAX) NULL;

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
IF COL_LENGTH('dbo.PRODUCTO', 'UNIDAD_MEDIDA') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [UNIDAD_MEDIDA] VARCHAR(30) NULL;
IF COL_LENGTH('dbo.PRODUCTO', 'FACTOR_CONVERSION_INVENTARIO') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [FACTOR_CONVERSION_INVENTARIO] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_PRODUCTO_FACTOR_CONVERSION_INV] DEFAULT (1);
UPDATE [dbo].[PRODUCTO] SET [UNIDAD_MEDIDA] = 'UNIDAD' WHERE [UNIDAD_MEDIDA] IS NULL OR LTRIM(RTRIM([UNIDAD_MEDIDA])) = '';
UPDATE [dbo].[PRODUCTO] SET [FACTOR_CONVERSION_INVENTARIO] = 1 WHERE [FACTOR_CONVERSION_INVENTARIO] IS NULL OR [FACTOR_CONVERSION_INVENTARIO] <= 0;

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

UPDATE emp
SET [nombre] = COALESCE(NULLIF(LTRIM(RTRIM(emp.[nombre])), ''), NULLIF(LTRIM(RTRIM(em.[razonSocial])), ''), CONCAT('Empresa ', emp.[idEmpresa])),
    [ruc] = COALESCE(NULLIF(LTRIM(RTRIM(emp.[ruc])), ''), NULLIF(LTRIM(RTRIM(em.[RUC])), '')),
    [IdTitular] = COALESCE(emp.[IdTitular], em.[id_usuario])
FROM [dbo].[EMPRESA] emp
OUTER APPLY (
    SELECT TOP (1) e.[razonSocial], e.[RUC], e.[id_usuario]
    FROM [dbo].[EMISOR] e
    WHERE e.[idEmpresa] = emp.[idEmpresa]
      AND ISNULL(e.[ESTADO], 0) = 1
      AND e.[id_usuario] IS NOT NULL
    ORDER BY e.[codigo]
) em
WHERE em.[id_usuario] IS NOT NULL
  AND (NULLIF(LTRIM(RTRIM(emp.[nombre])), '') IS NULL OR NULLIF(LTRIM(RTRIM(emp.[ruc])), '') IS NULL OR emp.[IdTitular] IS NULL);

INSERT INTO [dbo].[EMPRESA] ([idEmpresa], [nombre], [ruc], [estado])
SELECT DISTINCT
    e.[idEmpresa],
    COALESCE(NULLIF(LTRIM(RTRIM(MAX(e.[razonSocial]))), ''), CONCAT('Empresa ', e.[idEmpresa])),
    NULLIF(LTRIM(RTRIM(MAX(e.[RUC]))), ''),
    1
FROM [dbo].[EMISOR] e
WHERE e.[idEmpresa] IS NOT NULL
  AND ISNULL(e.[ESTADO], 0) = 1
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
  AND EXISTS (SELECT 1 FROM [dbo].[EMISOR] e WHERE e.[idEmpresa] = c.[idEmpresa] AND ISNULL(e.[ESTADO], 0) = 1)
  AND NOT EXISTS (
      SELECT 1 FROM [dbo].[EMPRESA] emp WHERE emp.[idEmpresa] = c.[idEmpresa]
  );

IF NOT EXISTS (SELECT 1 FROM [dbo].[EMPRESA])
BEGIN
    INSERT INTO [dbo].[EMPRESA] ([idEmpresa], [nombre], [estado])
    VALUES (1, 'Empresa principal', 1);
END;

UPDATE emp
SET [IdTitular] = em.[id_usuario]
FROM [dbo].[EMPRESA] emp
OUTER APPLY (
    SELECT TOP (1) e.[id_usuario]
    FROM [dbo].[EMISOR] e
    WHERE e.[idEmpresa] = emp.[idEmpresa]
      AND ISNULL(e.[ESTADO], 0) = 1
      AND e.[id_usuario] IS NOT NULL
    ORDER BY e.[codigo]
) em
WHERE emp.[IdTitular] IS NULL AND em.[id_usuario] IS NOT NULL;

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

INSERT INTO [dbo].[SUCURSAL] ([idSucursal], [idEmpresa], [nombre], [codigo], [estado])
SELECT 1, emp.[idEmpresa], 'Matriz', '001', 1
FROM [dbo].[EMPRESA] emp
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SUCURSAL] suc WHERE suc.[idEmpresa] = emp.[idEmpresa]);

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

        await EContaxMenuCatalog.EnsureAsync(context);
    }
}
