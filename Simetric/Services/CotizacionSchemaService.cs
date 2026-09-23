using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class CotizacionSchemaService(IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool schemaEnsured;

    public async Task EnsureSchemaAsync()
    {
        if (schemaEnsured) return;

        await SchemaLock.WaitAsync();
        try
        {
            if (schemaEnsured) return;

            await using var context = await dbFactory.CreateDbContextAsync();
            await context.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[dbo].[COTIZACION]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[COTIZACION] (
                        [ID_COTIZACION] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_COTIZACION] PRIMARY KEY,
                        [ID_USUARIO] INT NOT NULL,
                        [FECHA_CREACION] DATETIME2 NOT NULL,
                        [TOTAL_ESTIMADO] DECIMAL(18,2) NOT NULL
                    );
                    CREATE INDEX [IX_COTIZACION_ID_USUARIO_FECHA] ON [dbo].[COTIZACION] ([ID_USUARIO], [FECHA_CREACION] DESC);
                END;
                IF OBJECT_ID(N'[dbo].[COTIZACION_DETALLE]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[COTIZACION_DETALLE] (
                        [ID_DETALLE] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_COTIZACION_DETALLE] PRIMARY KEY,
                        [ID_COTIZACION] INT NOT NULL,
                        [CODIGO_PRODUCTO] INT NOT NULL,
                        [NOMBRE_PRODUCTO] NVARCHAR(250) NOT NULL,
                        [PRECIO_UNITARIO] DECIMAL(18,2) NOT NULL,
                        [CANTIDAD] INT NOT NULL,
                        [TOTAL_LINEA] DECIMAL(18,2) NOT NULL,
                        CONSTRAINT [FK_COTIZACION_DETALLE_COTIZACION] FOREIGN KEY ([ID_COTIZACION])
                            REFERENCES [dbo].[COTIZACION]([ID_COTIZACION]) ON DELETE CASCADE
                    );
                END;
                IF COL_LENGTH(N'dbo.COTIZACION', N'TITULO') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [TITULO] NVARCHAR(200) NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'ID_CLIENTE') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [ID_CLIENTE] INT NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'FORMA_PAGO') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [FORMA_PAGO] NVARCHAR(10) NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'DETALLE') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [DETALLE] NVARCHAR(1000) NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'ESTADO') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [ESTADO] NVARCHAR(30) NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'FECHA_APROBACION') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [FECHA_APROBACION] DATETIME2 NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'FECHA_VIGENCIA') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [FECHA_VIGENCIA] DATETIME2 NULL;
                IF COL_LENGTH(N'dbo.COTIZACION', N'CODFACTURA') IS NULL ALTER TABLE [dbo].[COTIZACION] ADD [CODFACTURA] INT NULL;
                IF COL_LENGTH(N'dbo.COTIZACION_DETALLE', N'DETALLE') IS NULL ALTER TABLE [dbo].[COTIZACION_DETALLE] ADD [DETALLE] NVARCHAR(500) NULL;
                IF COL_LENGTH(N'dbo.COTIZACION_DETALLE', N'DESCUENTO') IS NULL ALTER TABLE [dbo].[COTIZACION_DETALLE] ADD [DESCUENTO] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_COTIZACION_DETALLE_DESCUENTO] DEFAULT (0);
                IF COL_LENGTH(N'dbo.COTIZACION_DETALLE', N'TARIFA_IVA') IS NULL ALTER TABLE [dbo].[COTIZACION_DETALLE] ADD [TARIFA_IVA] INT NOT NULL CONSTRAINT [DF_COTIZACION_DETALLE_TARIFA_IVA] DEFAULT (0);
                """);
            schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
