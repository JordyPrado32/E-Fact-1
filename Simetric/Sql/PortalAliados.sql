/* Portal de Aliados Numerica - despliegue idempotente */

IF OBJECT_ID(N'dbo.VENDEDOR_BACKOFFICE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VENDEDOR_BACKOFFICE
    (
        idVendedor INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        nombre NVARCHAR(120) NOT NULL,
        codigoReferencia NVARCHAR(60) NOT NULL,
        activo BIT NOT NULL CONSTRAINT DF_VENDEDOR_BACKOFFICE_activo DEFAULT(1),
        esSistema BIT NOT NULL CONSTRAINT DF_VENDEDOR_BACKOFFICE_esSistema DEFAULT(0),
        idUsuarioCreacion INT NULL,
        fechaCreacion DATETIME NOT NULL CONSTRAINT DF_VENDEDOR_BACKOFFICE_fechaCreacion DEFAULT(GETDATE()),
        porcentajeBase DECIMAL(9,4) NOT NULL CONSTRAINT DF_VENDEDOR_BACKOFFICE_porcentajeBase DEFAULT(30)
    );
END

IF COL_LENGTH('dbo.VENDEDOR_BACKOFFICE', 'porcentajeBase') IS NULL
    ALTER TABLE dbo.VENDEDOR_BACKOFFICE ADD porcentajeBase DECIMAL(9,4) NOT NULL CONSTRAINT DF_VENDEDOR_BACKOFFICE_porcentajeBase DEFAULT(30);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_VENDEDOR_BACKOFFICE_codigoReferencia' AND object_id = OBJECT_ID(N'dbo.VENDEDOR_BACKOFFICE'))
    CREATE UNIQUE INDEX UX_VENDEDOR_BACKOFFICE_codigoReferencia ON dbo.VENDEDOR_BACKOFFICE(codigoReferencia);

IF COL_LENGTH('dbo.Usuarios', 'idVendedor') IS NULL
    ALTER TABLE dbo.Usuarios ADD idVendedor INT NULL;

IF OBJECT_ID(N'dbo.APP_SERVICIOS', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.APP_SERVICIOS WHERE Clave = N'portal-aliados')
        INSERT INTO dbo.APP_SERVICIOS (Clave, Nombre, Descripcion, RutaAcceso, RequiereSuscripcion, Estado, OrdenVisual, Icono, ColorHex)
        VALUES (N'portal-aliados', N'PORTAL DE ALIADOS', N'Gestion comercial de clientes, renovaciones y comisiones para aliados.', N'/aliados', 0, 1, 7, N'ri-team-line', N'#7B1E3A');
    ELSE
        UPDATE dbo.APP_SERVICIOS
        SET Nombre = N'PORTAL DE ALIADOS', Descripcion = N'Gestion comercial de clientes, renovaciones y comisiones para aliados.', RutaAcceso = N'/aliados', RequiereSuscripcion = 0, Estado = 1, Icono = N'ri-team-line', ColorHex = N'#7B1E3A'
        WHERE Clave = N'portal-aliados';
END

IF NOT EXISTS (SELECT 1 FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'Aliado Comercial')
    INSERT INTO dbo.TIPOUSUARIO (NOMBRETIPO, DESCRIPCION, ESTADO)
    VALUES (N'Aliado Comercial', N'Acceso externo al Portal de Aliados Numerica.', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'Administrador Portal de Aliados')
    INSERT INTO dbo.TIPOUSUARIO (NOMBRETIPO, DESCRIPCION, ESTADO)
    VALUES (N'Administrador Portal de Aliados', N'Administración interna del Portal de Aliados Numerica.', 1);

DECLARE @idTipoAliado INT = (SELECT TOP 1 IdTipoUsuario FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'Aliado Comercial' AND ESTADO = 1 ORDER BY IdTipoUsuario);
DECLARE @idTipoAdmin INT = (SELECT TOP 1 IdTipoUsuario FROM dbo.TIPOUSUARIO WHERE NOMBRETIPO = N'Administrador Portal de Aliados' AND ESTADO = 1 ORDER BY IdTipoUsuario);

IF NOT EXISTS (SELECT 1 FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAliado AND ESTADOROL = 1)
    INSERT INTO dbo.ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) VALUES (N'Aliado Comercial', @idTipoAliado, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAdmin AND ESTADOROL = 1)
    INSERT INTO dbo.ROLES (DESCRIPCIONROL, IDTIPOUSUARIO, ESTADOROL) VALUES (N'Administrador Portal de Aliados', @idTipoAdmin, 1);

DECLARE @idRolAliado INT = (SELECT TOP 1 IDROL FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAliado AND ESTADOROL = 1 ORDER BY IDROL);
DECLARE @idRolAdmin INT = (SELECT TOP 1 IDROL FROM dbo.ROLES WHERE IDTIPOUSUARIO = @idTipoAdmin AND ESTADOROL = 1 ORDER BY IDROL);
DECLARE @padre INT = (SELECT TOP 1 IDMENU FROM dbo.MENUS WHERE RUTAMENU = N'/aliados' AND ESTADOMENU = 1);

IF @padre IS NULL
BEGIN
    INSERT INTO dbo.MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU, MOSTRAR_EFACT, MOSTRAR_EDECLARA, orden_menu)
    VALUES (N'Portal de Aliados', N'/aliados', N'ri-team-line', 0, 1, 1, 0, 90);
    SET @padre = CONVERT(INT, SCOPE_IDENTITY());
END

INSERT INTO dbo.MENUS (NOMBREMENU, RUTAMENU, ICONOMENU, IDMENUPADRE, ESTADOMENU, MOSTRAR_EFACT, MOSTRAR_EDECLARA, orden_menu)
SELECT v.Nombre, v.Ruta, v.Icono, CASE WHEN v.Ruta = N'/aliados' THEN 0 ELSE @padre END, 1, 1, 0, v.Orden
FROM (VALUES
    (N'Inicio', N'/aliados', N'ri-dashboard-3-line', 1),
    (N'Mis clientes', N'/aliados/clientes', N'ri-user-3-line', 2),
    (N'Renovaciones', N'/aliados/renovaciones', N'ri-refresh-line', 3),
    (N'Comisiones', N'/aliados/comisiones', N'ri-hand-coin-line', 4),
    (N'Liquidaciones', N'/aliados/liquidaciones', N'ri-bank-card-line', 5),
    (N'Mi perfil', N'/aliados/perfil', N'ri-user-settings-line', 6),
    (N'Administración de aliados', N'/aliados/admin', N'ri-admin-line', 10)
) v(Nombre, Ruta, Icono, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MENUS m WHERE m.RUTAMENU = v.Ruta);

UPDATE dbo.MENUS
SET ESTADOMENU = 1, MOSTRAR_EFACT = 1, MOSTRAR_EDECLARA = 0
WHERE RUTAMENU IN (N'/aliados', N'/aliados/clientes', N'/aliados/renovaciones', N'/aliados/comisiones', N'/aliados/liquidaciones', N'/aliados/perfil', N'/aliados/admin');

INSERT INTO dbo.ROL_MENU (IDROL, IDMENU)
SELECT @idRolAliado, m.IDMENU
FROM dbo.MENUS m
WHERE m.RUTAMENU IN (N'/aliados', N'/aliados/clientes', N'/aliados/renovaciones', N'/aliados/comisiones', N'/aliados/liquidaciones', N'/aliados/perfil')
  AND NOT EXISTS (SELECT 1 FROM dbo.ROL_MENU rm WHERE rm.IDROL = @idRolAliado AND rm.IDMENU = m.IDMENU);

INSERT INTO dbo.ROL_MENU (IDROL, IDMENU)
SELECT @idRolAdmin, m.IDMENU
FROM dbo.MENUS m
WHERE m.RUTAMENU = N'/aliados/admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.ROL_MENU rm WHERE rm.IDROL = @idRolAdmin AND rm.IDMENU = m.IDMENU);

IF OBJECT_ID(N'dbo.ALIADO_RENOVACION_GESTION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_RENOVACION_GESTION
    (
        IdGestion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        IdFactura INT NOT NULL,
        FechaGestion DATETIME2 NOT NULL CONSTRAINT DF_ALIADO_GESTION_FECHA DEFAULT(SYSUTCDATETIME()),
        Resultado NVARCHAR(50) NOT NULL,
        OrigenGestion NVARCHAR(20) NOT NULL CONSTRAINT DF_ALIADO_GESTION_ORIGEN DEFAULT(N'Aliado'),
        Observacion NVARCHAR(500) NULL,
        ProximoSeguimiento DATE NULL,
        IdUsuario INT NOT NULL
    );
END

IF COL_LENGTH('dbo.ALIADO_RENOVACION_GESTION', 'OrigenGestion') IS NULL
    ALTER TABLE dbo.ALIADO_RENOVACION_GESTION ADD OrigenGestion NVARCHAR(20) NOT NULL CONSTRAINT DF_ALIADO_GESTION_ORIGEN DEFAULT(N'Aliado');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_GESTION_CANAL_FACTURA' AND object_id = OBJECT_ID(N'dbo.ALIADO_RENOVACION_GESTION'))
    CREATE INDEX IX_ALIADO_GESTION_CANAL_FACTURA ON dbo.ALIADO_RENOVACION_GESTION (IdVendedor, IdFactura, FechaGestion DESC);

IF OBJECT_ID(N'dbo.ALIADO_LIQUIDACION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_LIQUIDACION
    (
        IdLiquidacion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        Periodo NVARCHAR(20) NOT NULL,
        Total DECIMAL(18,2) NOT NULL,
        Fecha DATETIME2 NOT NULL,
        Estado NVARCHAR(30) NOT NULL,
        ReferenciaPago NVARCHAR(100) NULL
    );
END

IF OBJECT_ID(N'dbo.ALIADO_PORTAL_CONFIG', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_PORTAL_CONFIG
    (
        IdConfiguracion INT NOT NULL PRIMARY KEY,
        PorcentajeVentaNueva DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_VENTA DEFAULT(30),
        PorcentajeRenovacionAliado DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_RENOVACION_ALIADO DEFAULT(30),
        PorcentajeRenovacionNumerica DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_RENOVACION_NUMERICA DEFAULT(15),
        PorcentajeVentaDirecta DECIMAL(9,4) NOT NULL CONSTRAINT DF_ALIADO_CONFIG_VENTA_DIRECTA DEFAULT(0),
        DiasIntervencionNumerica INT NOT NULL CONSTRAINT DF_ALIADO_CONFIG_DIAS DEFAULT(15)
    );
END
IF NOT EXISTS (SELECT 1 FROM dbo.ALIADO_PORTAL_CONFIG WHERE IdConfiguracion = 1)
    INSERT INTO dbo.ALIADO_PORTAL_CONFIG (IdConfiguracion) VALUES (1);

IF OBJECT_ID(N'dbo.ALIADO_COMISION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ALIADO_COMISION
    (
        IdComision INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdVendedor INT NOT NULL,
        IdFactura INT NOT NULL,
        IdCliente INT NULL,
        TipoComision NVARCHAR(40) NOT NULL,
        BaseComisionable DECIMAL(18,2) NOT NULL,
        Porcentaje DECIMAL(9,4) NOT NULL,
        Valor DECIMAL(18,2) NOT NULL,
        Estado NVARCHAR(30) NOT NULL CONSTRAINT DF_ALIADO_COMISION_ESTADO DEFAULT(N'Generada'),
        FechaGeneracion DATETIME2 NOT NULL,
        FechaAprobacion DATETIME2 NULL,
        FechaPago DATETIME2 NULL,
        IdLiquidacion INT NULL,
        IdUsuarioAprobacion INT NULL,
        IdUsuarioPago INT NULL
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE UNIQUE INDEX UX_ALIADO_COMISION_VENDEDOR_FACTURA_TIPO ON dbo.ALIADO_COMISION (IdVendedor, IdFactura, TipoComision);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ALIADO_COMISION_VENDEDOR_ESTADO' AND object_id = OBJECT_ID(N'dbo.ALIADO_COMISION'))
    CREATE INDEX IX_ALIADO_COMISION_VENDEDOR_ESTADO ON dbo.ALIADO_COMISION (IdVendedor, Estado, FechaGeneracion DESC);
