using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class SqlPerformanceBootstrapService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SqlPerformanceBootstrapService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured)
            return;

        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured)
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();

            foreach (var statement in BuildIndexStatements())
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync(statement);
                }
                catch
                {
                    // Algunas instalaciones usan nombres físicos distintos; si un índice no aplica,
                    // continuamos con el resto para no bloquear el arranque.
                }
            }

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static IEnumerable<string> BuildIndexStatements()
    {
        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_CLIENTES_Usuario_NumeroIdentificacion'
                  AND object_id = OBJECT_ID('dbo.CLIENTES'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_CLIENTES_Usuario_NumeroIdentificacion
                ON dbo.CLIENTES (USUARIO, NUMEROIDENTIFICACION);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_CAJA_IdUsuario_Estado'
                  AND object_id = OBJECT_ID('dbo.CAJA'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_CAJA_IdUsuario_Estado
                ON dbo.CAJA (IdUsuario, Estado)
                INCLUDE (SerieFactura, NumCaja);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_EMISORES_IdUsuario_Estado_Codigo'
                  AND object_id = OBJECT_ID('dbo.EMISOR'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_EMISORES_IdUsuario_Estado_Codigo
                ON dbo.EMISOR (id_usuario, ESTADO, codigo)
                INCLUDE (pathCertificado, claveCertificado);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_CLIENTESCORREOS_CodCliente'
                  AND object_id = OBJECT_ID('dbo.ClientesCorreos'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_CLIENTESCORREOS_CodCliente
                ON dbo.ClientesCorreos (CodCliente)
                INCLUDE (Correo, Estado);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_FACTURA_IdUsuario_Codemisor_Serie_Codfactura'
                  AND object_id = OBJECT_ID('dbo.FACTURA'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_FACTURA_IdUsuario_Codemisor_Serie_Codfactura
                ON dbo.FACTURA (Idusuario, Codemisor, Serie, Codfactura DESC)
                INCLUDE (Numfactura, Valortotal, Fechaentrega, Autorizado, Estadoenviosri, Codclientes);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_DETALLEFACTURA_CodFactura'
                  AND object_id = OBJECT_ID('dbo.DETALLEFACTURA'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_DETALLEFACTURA_CodFactura
                ON dbo.DETALLEFACTURA (CODFACTURA)
                INCLUDE (CODPRODUCTO, CANTPRODUCTO, DESCRIPPRODUCTO, PRECIOPRODUCTO, DESCUENTO, VALORTPRODUCTO, VALORIVA, VALORTOTAL, TARIFA, VALORICE, COSTO, BONIFICACION);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_FACTURA_Idusuario_Guiaremision'
                  AND object_id = OBJECT_ID('dbo.FACTURA'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_FACTURA_Idusuario_Guiaremision
                ON dbo.FACTURA (Idusuario, GUIAREMISION);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_GUIAREMISION_IdUsuario_Serie_NumGuiaRemision'
                  AND object_id = OBJECT_ID('dbo.GUIAREMISION'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_GUIAREMISION_IdUsuario_Serie_NumGuiaRemision
                ON dbo.GUIAREMISION (idUsuario, serie, numGuiaRemision);
            END
            """;

        yield return """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = 'IX_Auditoria_IdUsuario_Fecha'
                  AND object_id = OBJECT_ID('dbo.Auditoria'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_Auditoria_IdUsuario_Fecha
                ON dbo.Auditoria (IdUsuario, Fecha DESC);
            END
            """;

        yield return """
            IF OBJECT_ID('dbo.REPORTEVENTABACKOFFICE', 'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes i
                   INNER JOIN sys.index_columns ic
                       ON ic.object_id = i.object_id
                      AND ic.index_id = i.index_id
                      AND ic.key_ordinal = 1
                   INNER JOIN sys.columns c
                       ON c.object_id = ic.object_id
                      AND c.column_id = ic.column_id
                   WHERE i.object_id = OBJECT_ID('dbo.REPORTEVENTABACKOFFICE')
                     AND c.name = 'IdReporte')
            BEGIN
                CREATE UNIQUE NONCLUSTERED INDEX IX_REPORTEVENTABACKOFFICE_IdReporte
                ON dbo.REPORTEVENTABACKOFFICE (IdReporte);
            END
            """;

        yield return """
            IF OBJECT_ID('dbo.REPORTEVENTABACKOFFICE', 'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE name = 'IX_REPORTEVENTABACKOFFICE_Fecha'
                     AND object_id = OBJECT_ID('dbo.REPORTEVENTABACKOFFICE'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_REPORTEVENTABACKOFFICE_Fecha
                ON dbo.REPORTEVENTABACKOFFICE (Fecha DESC)
                INCLUDE (IdReporte, Cliente, Producto, PlanPaquete, Valor, Canal, Vendedor, Estado, FormaPago, Observacion);
            END
            """;

        yield return """
            IF OBJECT_ID('dbo.NOTASCREDITO', 'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE name = 'IX_NOTACREDITO_Usuario'
                     AND object_id = OBJECT_ID('dbo.NOTASCREDITO'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_NOTACREDITO_Usuario
                ON dbo.NOTASCREDITO (usuario);
            END
            """;

        yield return """
            IF OBJECT_ID('dbo.USUARIOS', 'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE name = 'IX_USUARIOS_Email'
                     AND object_id = OBJECT_ID('dbo.USUARIOS'))
            BEGIN
                CREATE NONCLUSTERED INDEX IX_USUARIOS_Email
                ON dbo.USUARIOS (Email)
                INCLUDE (IdUsuario, Estado, IdTipoUsuario, SaldoDocumentos);
            END
            """;
    }
}
