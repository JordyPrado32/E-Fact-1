SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_ORDEN_COMPRA (
        id_orden_compra int IDENTITY(1,1) NOT NULL,
        id_empresa int NOT NULL,
        id_sucursal int NOT NULL,
        id_proveedor_cliente int NOT NULL,
        numero_orden nvarchar(30) NOT NULL,
        ruc_proveedor nvarchar(20) NOT NULL,
        nombre_proveedor nvarchar(250) NOT NULL,
        fecha_emision datetime2 NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_fecha_emision DEFAULT SYSDATETIME(),
        fecha_entrega_estimada datetime2 NULL,
        forma_pago nvarchar(100) NULL,
        plazo_pago int NULL,
        observacion nvarchar(1000) NULL,
        estado nvarchar(20) NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_estado DEFAULT N'BORRADOR',
        subtotal decimal(18,2) NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_subtotal DEFAULT 0,
        iva decimal(18,2) NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_iva DEFAULT 0,
        total decimal(18,2) NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_total DEFAULT 0,
        usuario_creacion int NOT NULL,
        fecha_creacion datetime2 NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_fecha_creacion DEFAULT SYSDATETIME(),
        usuario_actualizacion int NULL,
        fecha_actualizacion datetime2 NULL,
        CONSTRAINT PK_ECONTAX_ORDEN_COMPRA PRIMARY KEY (id_orden_compra),
        CONSTRAINT UQ_ECONTAX_ORDEN_COMPRA_NUMERO UNIQUE (id_empresa, numero_orden),
        CONSTRAINT FK_ECONTAX_ORDEN_COMPRA_SUCURSAL FOREIGN KEY (id_empresa, id_sucursal)
            REFERENCES dbo.SUCURSAL(idEmpresa, idSucursal)
    );
END;

IF OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA_DETALLE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_ORDEN_COMPRA_DETALLE (
        id_detalle int IDENTITY(1,1) NOT NULL,
        id_orden_compra int NOT NULL,
        codigo_producto int NOT NULL,
        codigo_principal nvarchar(100) NULL,
        descripcion nvarchar(500) NOT NULL,
        cantidad decimal(18,4) NOT NULL,
        precio_unitario decimal(18,4) NOT NULL,
        descuento decimal(18,2) NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_DETALLE_descuento DEFAULT 0,
        tarifa_iva int NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_DETALLE_tarifa_iva DEFAULT 0,
        subtotal decimal(18,2) NOT NULL,
        iva decimal(18,2) NOT NULL,
        total decimal(18,2) NOT NULL,
        CONSTRAINT PK_ECONTAX_ORDEN_COMPRA_DETALLE PRIMARY KEY (id_detalle),
        CONSTRAINT FK_ECONTAX_ORDEN_COMPRA_DETALLE_ORDEN FOREIGN KEY (id_orden_compra)
            REFERENCES dbo.ECONTAX_ORDEN_COMPRA(id_orden_compra) ON DELETE CASCADE,
        CONSTRAINT FK_ECONTAX_ORDEN_COMPRA_DETALLE_PRODUCTO FOREIGN KEY (codigo_producto)
            REFERENCES dbo.PRODUCTO(CODIGO)
    );
END;

IF OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA_HISTORIAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_ORDEN_COMPRA_HISTORIAL (
        id_historial int IDENTITY(1,1) NOT NULL,
        id_orden_compra int NOT NULL,
        estado_anterior nvarchar(20) NULL,
        estado_nuevo nvarchar(20) NOT NULL,
        observacion nvarchar(1000) NULL,
        usuario int NOT NULL,
        fecha datetime2 NOT NULL CONSTRAINT DF_ECONTAX_ORDEN_COMPRA_HISTORIAL_fecha DEFAULT SYSDATETIME(),
        CONSTRAINT PK_ECONTAX_ORDEN_COMPRA_HISTORIAL PRIMARY KEY (id_historial),
        CONSTRAINT FK_ECONTAX_ORDEN_COMPRA_HISTORIAL_ORDEN FOREIGN KEY (id_orden_compra)
            REFERENCES dbo.ECONTAX_ORDEN_COMPRA(id_orden_compra) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ECONTAX_ORDEN_COMPRA_EMPRESA_SUCURSAL_ESTADO' AND object_id = OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA'))
    CREATE INDEX IX_ECONTAX_ORDEN_COMPRA_EMPRESA_SUCURSAL_ESTADO ON dbo.ECONTAX_ORDEN_COMPRA (id_empresa, id_sucursal, estado);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ECONTAX_ORDEN_COMPRA_DETALLE_ORDEN' AND object_id = OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA_DETALLE'))
    CREATE INDEX IX_ECONTAX_ORDEN_COMPRA_DETALLE_ORDEN ON dbo.ECONTAX_ORDEN_COMPRA_DETALLE (id_orden_compra);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ECONTAX_ORDEN_COMPRA_HISTORIAL_ORDEN' AND object_id = OBJECT_ID(N'dbo.ECONTAX_ORDEN_COMPRA_HISTORIAL'))
    CREATE INDEX IX_ECONTAX_ORDEN_COMPRA_HISTORIAL_ORDEN ON dbo.ECONTAX_ORDEN_COMPRA_HISTORIAL (id_orden_compra, fecha);

COMMIT;
