    IF OBJECT_ID(N'dbo.ECONTAX_BODEGA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_BODEGA (
        id_bodega INT IDENTITY(1,1) NOT NULL,
        id_empresa INT NOT NULL,
        id_sucursal INT NOT NULL,
        nombre NVARCHAR(200) NOT NULL,
        codigo NVARCHAR(30) NOT NULL,
        direccion NVARCHAR(300) NULL,
        observacion NVARCHAR(500) NULL,
        estado BIT NOT NULL CONSTRAINT DF_ECONTAX_BODEGA_ESTADO DEFAULT (1),
        fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_ECONTAX_BODEGA_FECHA_CREACION DEFAULT SYSUTCDATETIME(),
        fecha_actualizacion DATETIME2 NULL,
        CONSTRAINT PK_ECONTAX_BODEGA PRIMARY KEY (id_bodega),
        CONSTRAINT FK_ECONTAX_BODEGA_SUCURSAL FOREIGN KEY (id_empresa, id_sucursal)
            REFERENCES dbo.SUCURSAL (idEmpresa, idSucursal),
        CONSTRAINT UX_ECONTAX_BODEGA_SUCURSAL_NOMBRE UNIQUE (id_empresa, id_sucursal, nombre),
        CONSTRAINT UX_ECONTAX_BODEGA_SUCURSAL_CODIGO UNIQUE (id_empresa, id_sucursal, codigo)
    );
END;
