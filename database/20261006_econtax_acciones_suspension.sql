-- Requiere 20261005_econtax_seguridad.sql. No modifica roles de otros servicios.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ECONTAX_PERFIL', N'U') IS NULL OR OBJECT_ID(N'dbo.ECONTAX_USUARIO_CONTEXTO', N'U') IS NULL
    THROW 50001, 'Ejecute primero 20261005_econtax_seguridad.sql.', 1;

IF COL_LENGTH('dbo.ECONTAX_PERFIL', 'AccionesJson') IS NULL
    ALTER TABLE dbo.ECONTAX_PERFIL ADD AccionesJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'AccionesJson') IS NULL
    ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD AccionesJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.ECONTAX_USUARIO_CONTEXTO', 'Suspendido') IS NULL
    ALTER TABLE dbo.ECONTAX_USUARIO_CONTEXTO ADD Suspendido bit NOT NULL
        CONSTRAINT DF_ECONTAX_USUARIO_CONTEXTO_Suspendido DEFAULT 0;

-- NULL conserva las acciones existentes. Al editar se guardan acciones explícitas por menú.
COMMIT TRANSACTION;
