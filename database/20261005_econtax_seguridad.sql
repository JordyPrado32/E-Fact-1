-- Puede ejecutarse tanto en instalaciones existentes como en aquellas donde
-- todavía no se ejecutó el bootstrap de organización de E-Contax.
-- Los titulares ambiguos se deben resolver en @Titulares; el script no elige uno arbitrariamente.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.EMPRESA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EMPRESA (
        idEmpresa int NOT NULL PRIMARY KEY,
        nombre nvarchar(200) NOT NULL,
        ruc nvarchar(20) NULL,
        estado bit NOT NULL CONSTRAINT DF_EMPRESA_estado DEFAULT 1,
        fechaCreacion datetime2 NOT NULL CONSTRAINT DF_EMPRESA_fechaCreacion DEFAULT SYSUTCDATETIME(),
        fechaActualizacion datetime2 NULL
    );
END;
IF OBJECT_ID(N'dbo.SUCURSAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SUCURSAL (
        idSucursal int NOT NULL,
        idEmpresa int NOT NULL,
        nombre nvarchar(200) NOT NULL,
        codigo nvarchar(20) NULL,
        direccion nvarchar(300) NULL,
        estado bit NOT NULL CONSTRAINT DF_SUCURSAL_estado DEFAULT 1,
        fechaCreacion datetime2 NOT NULL CONSTRAINT DF_SUCURSAL_fechaCreacion DEFAULT SYSUTCDATETIME(),
        fechaActualizacion datetime2 NULL,
        CONSTRAINT PK_SUCURSAL PRIMARY KEY (idEmpresa, idSucursal),
        CONSTRAINT FK_SUCURSAL_EMPRESA FOREIGN KEY (idEmpresa) REFERENCES dbo.EMPRESA(idEmpresa)
    );
END;
IF OBJECT_ID(N'dbo.ECONTAX_USUARIO_CONTEXTO', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_USUARIO_CONTEXTO (
        id_usuario int NOT NULL PRIMARY KEY,
        id_empresa int NOT NULL,
        id_sucursal int NULL,
        estado bit NOT NULL CONSTRAINT DF_ECONTAX_USUARIO_CONTEXTO_estado DEFAULT 1,
        fecha_creacion datetime2 NOT NULL CONSTRAINT DF_ECONTAX_USUARIO_CONTEXTO_fecha_creacion DEFAULT SYSUTCDATETIME(),
        fecha_actualizacion datetime2 NULL,
        CONSTRAINT FK_ECONTAX_USUARIO_CONTEXTO_USUARIO FOREIGN KEY (id_usuario) REFERENCES dbo.Usuarios(IdUsuario),
        CONSTRAINT FK_ECONTAX_USUARIO_CONTEXTO_EMPRESA FOREIGN KEY (id_empresa) REFERENCES dbo.EMPRESA(idEmpresa),
        CONSTRAINT FK_ECONTAX_USUARIO_CONTEXTO_SUCURSAL FOREIGN KEY (id_empresa, id_sucursal) REFERENCES dbo.SUCURSAL(idEmpresa, idSucursal)
    );
END;
IF COL_LENGTH('dbo.EMPRESA', 'IdTitular') IS NULL ALTER TABLE dbo.EMPRESA ADD IdTitular int NULL;
IF COL_LENGTH('dbo.EMPRESA', 'MenusJson') IS NULL ALTER TABLE dbo.EMPRESA ADD MenusJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.SUCURSAL', 'MenusJson') IS NULL ALTER TABLE dbo.SUCURSAL ADD MenusJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'IdPerfil') IS NULL ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD IdPerfil int NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'EsAdminSucursal') IS NULL
    ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD EsAdminSucursal bit NOT NULL CONSTRAINT DF_ECONTAX_ADMIN DEFAULT 0;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'MenusJson') IS NULL ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD MenusJson nvarchar(max) NULL;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Titulares TABLE (IdEmpresa int PRIMARY KEY, IdUsuario int NOT NULL);
-- Para empresas cuyo titular no se pueda deducir: INSERT @Titulares VALUES (idEmpresa, idUsuarioComprador);
UPDATE e SET IdTitular = t.IdUsuario FROM dbo.EMPRESA e JOIN @Titulares t ON t.IdEmpresa = e.idEmpresa WHERE e.IdTitular IS NULL;
;WITH Candidatos AS (
    SELECT DISTINCT em.idEmpresa, CASE WHEN u.estadoAsociado = 1 THEN COALESCE(u.idJefe, u.IdUsuario) ELSE u.IdUsuario END IdUsuario
    FROM dbo.EMISOR em JOIN dbo.Usuarios u ON u.IdUsuario = em.id_usuario
    WHERE em.idEmpresa IS NOT NULL
), Unicos AS (
    SELECT idEmpresa, MIN(IdUsuario) IdUsuario FROM Candidatos GROUP BY idEmpresa HAVING COUNT(*) = 1
)
UPDATE e SET IdTitular = c.IdUsuario FROM dbo.EMPRESA e JOIN Unicos c ON c.idEmpresa = e.idEmpresa WHERE e.IdTitular IS NULL;
;WITH Candidatos AS (
    SELECT DISTINCT uc.id_empresa, uc.id_usuario
    FROM dbo.ECONTAX_USUARIO_CONTEXTO uc
    JOIN dbo.ECONTAX_USUARIO_ROL ur ON ur.id_usuario = uc.id_usuario AND ur.estado = 1
    JOIN dbo.rol r ON r.id_rol = ur.id_rol AND r.estado_rol = 1
    WHERE uc.estado = 1 AND UPPER(LTRIM(RTRIM(r.nombre_rol))) IN ('JEFE_EMPRESA', 'ADMIN_EMPRESA', 'ADMINISTRADOR_EMPRESA')
), Unicos AS (
    SELECT id_empresa, MIN(id_usuario) IdUsuario FROM Candidatos GROUP BY id_empresa HAVING COUNT(*) = 1
)
UPDATE e SET IdTitular = c.IdUsuario FROM dbo.EMPRESA e JOIN Unicos c ON c.id_empresa = e.idEmpresa WHERE e.IdTitular IS NULL;
IF EXISTS (SELECT 1 FROM dbo.EMPRESA WHERE estado = 1 AND IdTitular IS NULL)
BEGIN
    SELECT idEmpresa, nombre FROM dbo.EMPRESA WHERE estado = 1 AND IdTitular IS NULL;
    ROLLBACK;
    THROW 50001, 'Define los titulares pendientes en @Titulares y vuelve a ejecutar.', 1;
END;
IF EXISTS (SELECT IdTitular FROM dbo.EMPRESA WHERE IdTitular IS NOT NULL GROUP BY IdTitular HAVING COUNT(*) > 1)
BEGIN
    ROLLBACK;
    THROW 50002, 'Una cuenta no puede ser titular de varias empresas. Revisa @Titulares.', 1;
END;
IF OBJECT_ID('dbo.ECONTAX_PERFIL', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_PERFIL (
        IdPerfil int IDENTITY PRIMARY KEY, IdEmpresa int NOT NULL, IdSucursal int NULL,
        Nombre nvarchar(200) NOT NULL, MenusJson nvarchar(max) NOT NULL, Estado bit NOT NULL DEFAULT 1,
        CONSTRAINT FK_ECONTAX_PERFIL_EMPRESA FOREIGN KEY (IdEmpresa) REFERENCES dbo.EMPRESA(idEmpresa),
        CONSTRAINT FK_ECONTAX_PERFIL_SUCURSAL FOREIGN KEY (IdEmpresa, IdSucursal) REFERENCES dbo.SUCURSAL(idEmpresa, idSucursal),
        CONSTRAINT CK_ECONTAX_PERFIL_JSON CHECK (ISJSON(MenusJson) = 1),
        CONSTRAINT UQ_ECONTAX_PERFIL_EMPRESA UNIQUE (IdEmpresa, IdPerfil)
    );
END;
IF OBJECT_ID('dbo.ECONTAX_INVITACION', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ECONTAX_INVITACION (
        IdInvitacion int IDENTITY PRIMARY KEY, IdEmpresa int NOT NULL, IdSucursal int NOT NULL,
        IdPerfil int NOT NULL, IdInvitador int NOT NULL, EsAdminSucursal bit NOT NULL,
        Email nvarchar(320) NOT NULL, TokenHash nvarchar(64) NOT NULL,
        Vence datetime2 NOT NULL, Aceptada datetime2 NULL, Cancelada bit NOT NULL DEFAULT 0,
        CONSTRAINT FK_ECONTAX_INVITACION_SUCURSAL FOREIGN KEY (IdEmpresa, IdSucursal) REFERENCES dbo.SUCURSAL(idEmpresa, idSucursal),
        CONSTRAINT FK_ECONTAX_INVITACION_PERFIL FOREIGN KEY (IdEmpresa, IdPerfil) REFERENCES dbo.ECONTAX_PERFIL(IdEmpresa, IdPerfil),
        CONSTRAINT FK_ECONTAX_INVITACION_ACTOR FOREIGN KEY (IdInvitador) REFERENCES dbo.Usuarios(IdUsuario),
        CONSTRAINT UQ_ECONTAX_INVITACION_TOKEN UNIQUE (TokenHash)
    );
    CREATE INDEX IX_ECONTAX_INVITACION_EMAIL ON dbo.ECONTAX_INVITACION(Email, Cancelada, Aceptada, Vence);
END;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ECONTAX_EMPRESA_TITULAR')
    ALTER TABLE dbo.EMPRESA ADD CONSTRAINT FK_ECONTAX_EMPRESA_TITULAR FOREIGN KEY (IdTitular) REFERENCES dbo.Usuarios(IdUsuario);
-- Conservar los roles existentes como perfiles independientes de cada empresa.
INSERT dbo.ECONTAX_PERFIL (IdEmpresa, Nombre, MenusJson)
SELECT DISTINCT uc.id_empresa, r.nombre_rol,
    COALESCE((SELECT '[' + STRING_AGG(CONVERT(nvarchar(max), rm.id_menu), ',') + ']' FROM dbo.ECONTAX_ROL_MENU rm WHERE rm.id_rol = r.id_rol), '[]')
FROM dbo.ECONTAX_USUARIO_CONTEXTO uc
JOIN dbo.ECONTAX_USUARIO_ROL ur ON ur.id_usuario = uc.id_usuario AND ur.estado = 1
JOIN dbo.rol r ON r.id_rol = ur.id_rol AND r.estado_rol = 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.ECONTAX_PERFIL p WHERE p.IdEmpresa = uc.id_empresa AND p.Nombre = r.nombre_rol);
INSERT dbo.ECONTAX_PERFIL (IdEmpresa, Nombre, MenusJson)
SELECT idEmpresa, 'Empleado', '[]' FROM dbo.EMPRESA e
WHERE NOT EXISTS (SELECT 1 FROM dbo.ECONTAX_PERFIL p WHERE p.IdEmpresa = e.idEmpresa AND p.Nombre = 'Empleado');
UPDATE uc SET IdPerfil = COALESCE(p.IdPerfil, empleado.IdPerfil),
    EsAdminSucursal = CASE WHEN UPPER(LTRIM(RTRIM(r.nombre_rol))) IN ('ADMIN_SUCURSAL', 'ADMINISTRADOR_SUCURSAL') THEN 1 ELSE 0 END
FROM dbo.ECONTAX_USUARIO_CONTEXTO uc
OUTER APPLY (SELECT TOP (1) r.nombre_rol FROM dbo.ECONTAX_USUARIO_ROL ur JOIN dbo.rol r ON r.id_rol = ur.id_rol
    WHERE ur.id_usuario = uc.id_usuario AND ur.estado = 1 AND r.estado_rol = 1 ORDER BY r.id_rol) r
OUTER APPLY (SELECT TOP (1) IdPerfil FROM dbo.ECONTAX_PERFIL WHERE IdEmpresa = uc.id_empresa AND Nombre = r.nombre_rol ORDER BY IdPerfil) p
OUTER APPLY (SELECT TOP (1) IdPerfil FROM dbo.ECONTAX_PERFIL WHERE IdEmpresa = uc.id_empresa AND Nombre = 'Empleado' ORDER BY IdPerfil) empleado
WHERE uc.IdPerfil IS NULL;
IF EXISTS (SELECT 1 FROM dbo.EMPRESA e JOIN dbo.ECONTAX_USUARIO_CONTEXTO uc ON uc.id_usuario = e.IdTitular
    WHERE uc.estado = 1 AND uc.id_empresa <> e.idEmpresa)
BEGIN
    ROLLBACK;
    THROW 50003, 'Un titular ya pertenece a otra empresa. Corrige las asignaciones antes de migrar.', 1;
END;
INSERT dbo.ECONTAX_USUARIO_CONTEXTO (id_usuario, id_empresa, id_sucursal, estado)
SELECT e.IdTitular, e.idEmpresa, s.idSucursal, 1 FROM dbo.EMPRESA e
OUTER APPLY (SELECT TOP (1) idSucursal FROM dbo.SUCURSAL WHERE idEmpresa = e.idEmpresa AND estado = 1 ORDER BY idSucursal) s
WHERE e.IdTitular IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ECONTAX_USUARIO_CONTEXTO WHERE id_usuario = e.IdTitular);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_ECONTAX_CONTEXTO_PERFIL')
    ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD CONSTRAINT FK_ECONTAX_CONTEXTO_PERFIL FOREIGN KEY (id_empresa, IdPerfil) REFERENCES dbo.ECONTAX_PERFIL(IdEmpresa, IdPerfil);
COMMIT;
