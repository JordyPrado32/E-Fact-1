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

IF OBJECT_ID(N'[dbo].[ECONTAX_ORDEN_COMPRA]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_ORDEN_COMPRA', 'numero_factura_proveedor') IS NULL
    ALTER TABLE [dbo].[ECONTAX_ORDEN_COMPRA] ADD [numero_factura_proveedor] VARCHAR(50) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_ORDEN_COMPRA]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_ORDEN_COMPRA', 'clave_acceso_proveedor') IS NULL
    ALTER TABLE [dbo].[ECONTAX_ORDEN_COMPRA] ADD [clave_acceso_proveedor] VARCHAR(60) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_RECEPCION_COMPRA_DETALLE', 'numero_lote') IS NULL
    ALTER TABLE [dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE] ADD [numero_lote] VARCHAR(80) NULL;
IF OBJECT_ID(N'[dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ECONTAX_RECEPCION_COMPRA_DETALLE', 'fecha_vencimiento') IS NULL
    ALTER TABLE [dbo].[ECONTAX_RECEPCION_COMPRA_DETALLE] ADD [fecha_vencimiento] DATE NULL;
IF OBJECT_ID(N'[dbo].[PRODUCTO]', N'U') IS NOT NULL AND COL_LENGTH('dbo.PRODUCTO', 'UNIDAD_MEDIDA') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [UNIDAD_MEDIDA] VARCHAR(30) NULL;
IF OBJECT_ID(N'[dbo].[PRODUCTO]', N'U') IS NOT NULL AND COL_LENGTH('dbo.PRODUCTO', 'FACTOR_CONVERSION_INVENTARIO') IS NULL
    ALTER TABLE [dbo].[PRODUCTO] ADD [FACTOR_CONVERSION_INVENTARIO] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_PRODUCTO_FACTOR_CONVERSION_INV] DEFAULT (1);
