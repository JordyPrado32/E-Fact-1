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
                """);
            schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
