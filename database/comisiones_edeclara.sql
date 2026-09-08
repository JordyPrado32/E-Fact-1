/* Módulo Comisiones: ejecutar en la base de E-Fact. No modifica facturación existente. */
IF OBJECT_ID('dbo.DECLARA_COMISION_INVOLUCRADO','U') IS NULL
BEGIN
 CREATE TABLE dbo.DECLARA_COMISION_INVOLUCRADO (
  Id int IDENTITY PRIMARY KEY, IdEmpresa int NOT NULL, IdSucursal int NULL, Tipo varchar(20) NOT NULL,
  IdEmpleado int NULL, Identificacion nvarchar(30) NOT NULL, Nombre nvarchar(200) NOT NULL, Telefono nvarchar(40) NULL, Correo nvarchar(150) NULL,
  PorcentajePredeterminado decimal(9,4) NOT NULL CONSTRAINT CK_DECLARA_COMISION_INV_PCT CHECK(PorcentajePredeterminado BETWEEN 0 AND 100), ValorPredeterminado decimal(18,2) NOT NULL DEFAULT(0),
  TipoCalculo varchar(20) NOT NULL, BaseCalculo varchar(20) NOT NULL, FechaVigencia date NOT NULL, Activo bit NOT NULL DEFAULT(1),
  Observaciones nvarchar(1000) NULL, DatosBancarios nvarchar(1000) NULL, CreadoPor int NULL, ModificadoPor int NULL, FechaCreacion datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()), FechaModificacion datetime2 NULL
 );
 CREATE UNIQUE INDEX UX_DECLARA_COMISION_INV_IDENT ON dbo.DECLARA_COMISION_INVOLUCRADO(IdEmpresa,Identificacion);
END;
IF COL_LENGTH('dbo.DECLARA_COMISION_INVOLUCRADO','ValorPredeterminado') IS NULL
 ALTER TABLE dbo.DECLARA_COMISION_INVOLUCRADO ADD ValorPredeterminado decimal(18,2) NOT NULL CONSTRAINT DF_DECLARA_COMISION_INV_VALOR DEFAULT(0);
IF OBJECT_ID('dbo.DECLARA_COMISION_CONFIG','U') IS NULL
 CREATE TABLE dbo.DECLARA_COMISION_CONFIG (IdEmpresa int PRIMARY KEY, LimitePorFactura decimal(18,2) NOT NULL DEFAULT(0), ReglaRedondeo varchar(40) NOT NULL DEFAULT('2 decimales'), MomentoPendiente varchar(30) NOT NULL DEFAULT('Al autorizar'), PermitirModificarPorcentaje bit NOT NULL DEFAULT(0), RequiereAutorizacionSobreLimite bit NOT NULL DEFAULT(1));
IF OBJECT_ID('dbo.DECLARA_COMISION_CONFIG_HISTORIAL','U') IS NULL
 CREATE TABLE dbo.DECLARA_COMISION_CONFIG_HISTORIAL (Id bigint IDENTITY PRIMARY KEY, IdInvolucrado int NOT NULL, PorcentajeAnterior decimal(9,4) NULL, PorcentajeNuevo decimal(9,4) NOT NULL, UsuarioId int NULL, Fecha datetime2 NOT NULL, Motivo nvarchar(500) NOT NULL);
IF OBJECT_ID('dbo.DECLARA_COMISION_MOVIMIENTO','U') IS NULL
 CREATE TABLE dbo.DECLARA_COMISION_MOVIMIENTO (Id bigint IDENTITY PRIMARY KEY, IdEmpresa int NOT NULL, IdFactura int NOT NULL, IdInvolucrado int NOT NULL, BaseUtilizada varchar(20) NOT NULL, Porcentaje decimal(9,4) NOT NULL, ValorFacturado decimal(18,2) NOT NULL, ComisionGenerada decimal(18,2) NOT NULL, Estado varchar(20) NOT NULL, FechaGeneracion datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()), FechaPago datetime2 NULL, MetodoPago varchar(40) NULL, ReferenciaPago nvarchar(100) NULL, Motivo nvarchar(500) NULL, UsuarioResponsable int NULL, IdempotencyKey nvarchar(100) NULL, CONSTRAINT UX_DECLARA_COMISION_MOV_IDEMP UNIQUE(IdFactura,IdInvolucrado));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DECLARA_COMISION_MOVIMIENTO_CONSULTA' AND object_id=OBJECT_ID('dbo.DECLARA_COMISION_MOVIMIENTO'))
 CREATE INDEX IX_DECLARA_COMISION_MOVIMIENTO_CONSULTA ON dbo.DECLARA_COMISION_MOVIMIENTO(IdEmpresa,FechaGeneracion DESC,Estado) INCLUDE(IdFactura,IdInvolucrado,ComisionGenerada,ValorFacturado);
/* Menú solo E-Declara: el filtro MOSTRAR_EFACT=0 impide su uso en e-Fact. */
DECLARE @padre int=(SELECT TOP 1 IDMENU FROM MENUS WHERE NOMBREMENU='Comisiones' AND MOSTRAR_EDECLARA=1);
IF @padre IS NULL BEGIN INSERT MENUS(NOMBREMENU,RUTAMENU,ICONOMENU,IDMENUPADRE,ESTADOMENU,MOSTRAR_EFACT,MOSTRAR_EDECLARA) VALUES('Comisiones','/e-declara/comisiones/involucrados','ri-hand-coin-line',NULL,1,0,1); SET @padre=SCOPE_IDENTITY(); END ELSE UPDATE MENUS SET RUTAMENU='/e-declara/comisiones/involucrados',MOSTRAR_EFACT=0,MOSTRAR_EDECLARA=1 WHERE IDMENU=@padre;
UPDATE MENUS SET ESTADOMENU=0 WHERE IDMENUPADRE=@padre AND NOMBREMENU IN ('Movimientos','Reportes','Configuración','Configuracion');
UPDATE MENUS SET ESTADOMENU=1,MOSTRAR_EFACT=0,MOSTRAR_EDECLARA=1 WHERE IDMENUPADRE=@padre AND RUTAMENU IN ('/e-declara/comisiones/involucrados','/e-declara/comisiones/herramientas');
UPDATE MENUS SET NOMBREMENU='Historial comisiones',ICONOMENU='ri-history-line' WHERE IDMENUPADRE=@padre AND RUTAMENU='/e-declara/comisiones/herramientas';
INSERT MENUS(NOMBREMENU,RUTAMENU,ICONOMENU,IDMENUPADRE,ESTADOMENU,MOSTRAR_EFACT,MOSTRAR_EDECLARA) SELECT v.Nombre,v.Ruta,v.Icono,@padre,1,0,1 FROM (VALUES ('Involucrados','/e-declara/comisiones/involucrados','ri-group-line'),('Historial comisiones','/e-declara/comisiones/herramientas','ri-history-line')) v(Nombre,Ruta,Icono) WHERE NOT EXISTS(SELECT 1 FROM MENUS m WHERE m.RUTAMENU=v.Ruta);
INSERT ROL_MENU(IDROL,IDMENU) SELECT r.IDROL,m.IDMENU FROM ROLES r CROSS JOIN MENUS m WHERE m.NOMBREMENU='Comisiones' AND NOT EXISTS(SELECT 1 FROM ROL_MENU x WHERE x.IDROL=r.IDROL AND x.IDMENU=m.IDMENU);
INSERT ROL_MENU(IDROL,IDMENU) SELECT r.IDROL,m.IDMENU FROM ROLES r CROSS JOIN MENUS m JOIN MENUS p ON p.IDMENU=m.IDMENUPADRE WHERE p.NOMBREMENU='Comisiones' AND NOT EXISTS(SELECT 1 FROM ROL_MENU x WHERE x.IDROL=r.IDROL AND x.IDMENU=m.IDMENU);
