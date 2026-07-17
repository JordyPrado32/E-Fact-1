using Microsoft.EntityFrameworkCore;
using Simetric.Data;

namespace Simetric.Services;

public sealed class FacturaStoredProcedureBootstrapService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public const string DetalleTypeName = "dbo.FacturaDetalleType";
    public const string GuardarProcName = "dbo.sp_GuardarFacturaCompleta";

    public FacturaStoredProcedureBootstrapService(IDbContextFactory<AppDbContext> dbFactory)
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

            await context.Database.ExecuteSqlRawAsync(BuildCreateTypeSql());
            await context.Database.ExecuteSqlRawAsync(BuildCreateProcedureSql());

            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static string BuildCreateTypeSql() =>
        """
        IF TYPE_ID(N'dbo.FacturaDetalleType') IS NULL
        EXEC('
            CREATE TYPE dbo.FacturaDetalleType AS TABLE
            (
                Codproducto INT NOT NULL,
                Codprincipal NVARCHAR(100) NULL,
                Codauxiliar NVARCHAR(100) NULL,
                Cantproducto DECIMAL(18, 2) NOT NULL,
                Descripproducto NVARCHAR(500) NULL,
                Precioproducto DECIMAL(18, 2) NOT NULL,
                Descuento DECIMAL(18, 2) NULL,
                Valortproducto DECIMAL(18, 2) NOT NULL,
                Valoriva DECIMAL(18, 2) NOT NULL,
                Valortotal DECIMAL(18, 2) NOT NULL,
                Tarifa INT NOT NULL,
                Valorice DECIMAL(18, 2) NULL,
                Costo DECIMAL(18, 2) NULL,
                Bonificacion INT NULL
            );
        ');
        """;

    private static string BuildCreateProcedureSql() =>
        """
        CREATE OR ALTER PROCEDURE dbo.sp_GuardarFacturaCompleta
            @IdUsuarioSaldo INT,
            @Codclave NVARCHAR(255) = NULL,
            @Codclientes INT,
            @Codemisor INT,
            @Coddocumento INT,
            @Fechaentrega DATETIME,
            @Numfactura NVARCHAR(50),
            @Serie NVARCHAR(20),
            @Guiaremision NVARCHAR(50) = NULL,
            @Subtotal12 DECIMAL(18, 2),
            @Subtotal0 DECIMAL(18, 2),
            @Subtotal DECIMAL(18, 2),
            @Descuentos DECIMAL(18, 2),
            @Iva DECIMAL(18, 2),
            @Valortotal DECIMAL(18, 2),
            @DescuentoGlobalPct DECIMAL(18, 6) = NULL,
            @DescuentoGlobalValor DECIMAL(18, 2) = NULL,
            @Correoad NVARCHAR(MAX) = NULL,
            @Detalleextra NVARCHAR(MAX) = NULL,
            @Notas NVARCHAR(MAX) = NULL,
            @Estado BIT = 1,
            @Idusuario INT,
            @Detalles dbo.FacturaDetalleType READONLY,
            @PlanIlimitadoActivo BIT = 0,
            @Tipopago NVARCHAR(20) = NULL,
            @Tiempocredito INT = NULL,
            @Ambiente INT = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            SET @Guiaremision = NULLIF(LTRIM(RTRIM(@Guiaremision)), '');

            IF @Guiaremision IS NOT NULL
            BEGIN
                DECLARE @GuiaNormalizada NVARCHAR(15) = REPLACE(@Guiaremision, '-', '');

                IF LEN(@GuiaNormalizada) <> 15
                BEGIN
                    THROW 51001, 'La guia de remision debe tener el formato 001-001-000000001.', 1;
                END

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.FACTURA WITH (UPDLOCK, HOLDLOCK)
                    WHERE Idusuario = @Idusuario
                      AND GUIAREMISION = @Guiaremision
                )
                BEGIN
                    THROW 51002, 'La guia de remision ya esta en uso.', 1;
                END

                DECLARE @SerieGuiaSinSeparador NVARCHAR(6) = LEFT(@GuiaNormalizada, 6);
                DECLARE @SerieGuiaFormateada NVARCHAR(7) = CONCAT(LEFT(@SerieGuiaSinSeparador, 3), '-', RIGHT(@SerieGuiaSinSeparador, 3));
                DECLARE @SecuencialGuia NVARCHAR(9) = RIGHT(@GuiaNormalizada, 9);

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.GUIAREMISION WITH (UPDLOCK, HOLDLOCK)
                    WHERE idUsuario = @Idusuario
                      AND numGuiaRemision = @SecuencialGuia
                      AND serie IN (@SerieGuiaSinSeparador, @SerieGuiaFormateada)
                )
                BEGIN
                    THROW 51003, 'La guia de remision ya fue registrada.', 1;
                END
            END

            IF @PlanIlimitadoActivo = 0
            BEGIN
                UPDATE dbo.Usuarios
                SET SaldoDocumentos = SaldoDocumentos - 1
                WHERE IdUsuario = @IdUsuarioSaldo
                  AND ISNULL(SaldoDocumentos, 0) > 0;

                IF @@ROWCOUNT = 0
                BEGIN
                    THROW 51000, 'Saldo de documentos agotado.', 1;
                END
            END

            DECLARE @FacturaInsertada TABLE (Codfactura INT NOT NULL);

            INSERT INTO dbo.FACTURA
            (
                Codclave,
                Codclientes,
                Codemisor,
                Coddocumento,
                Fechaentrega,
                NUMFACTURA,
                GUIAREMISION,
                Subtotal12,
                Subtotal0,
                Subtotal,
                Descuentos,
                Iva,
                Valortotal,
                Idusuario,
                Serie,
                Estado,
                CORREOAD,
                Detalleextra,
                Notas,
                DescuentoGlobalPct,
                DescuentoGlobalValor,
                Tipopago,
                Tiempocredito,
                Ambiente
            )
            OUTPUT INSERTED.CODFACTURA INTO @FacturaInsertada(Codfactura)
            VALUES
            (
                @Codclave,
                @Codclientes,
                @Codemisor,
                @Coddocumento,
                @Fechaentrega,
                @Numfactura,
                @Guiaremision,
                @Subtotal12,
                @Subtotal0,
                @Subtotal,
                @Descuentos,
                @Iva,
                @Valortotal,
                @Idusuario,
                @Serie,
                @Estado,
                @Correoad,
                @Detalleextra,
                @Notas,
                @DescuentoGlobalPct,
                @DescuentoGlobalValor,
                @Tipopago,
                @Tiempocredito,
                @Ambiente
            );

            DECLARE @Codfactura INT = (SELECT TOP (1) Codfactura FROM @FacturaInsertada);

            INSERT INTO dbo.DETALLEFACTURA
            (
                CODFACTURA,
                CODPRODUCTO,
                CODPRINCIPAL,
                CODAUXILIAR,
                CANTPRODUCTO,
                DESCRIPPRODUCTO,
                PRECIOPRODUCTO,
                DESCUENTO,
                VALORTPRODUCTO,
                VALORIVA,
                VALORTOTAL,
                TARIFA,
                VALORICE,
                COSTO,
                BONIFICACION
            )
            SELECT
                @Codfactura,
                d.Codproducto,
                d.Codprincipal,
                d.Codauxiliar,
                d.Cantproducto,
                d.Descripproducto,
                d.Precioproducto,
                d.Descuento,
                d.Valortproducto,
                d.Valoriva,
                d.Valortotal,
                d.Tarifa,
                d.Valorice,
                d.Costo,
                d.Bonificacion
            FROM @Detalles d;

            SELECT @Codfactura AS Codfactura;
        END
        """;
}
