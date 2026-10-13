using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models.EDeclara;

namespace Simetric.Services.EDeclara;

public sealed class EDeclaraDeclaracionesService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;
    private readonly string _connectionString;
    private readonly EDeclaraComprasService _comprasService;

    private sealed class DocumentoAnalisis
    {
        public int TipoDocumento { get; set; }
        public string Casilla { get; set; } = string.Empty;
        public int? TipoFacturacion { get; set; }
        public string Serie { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public string Ruc { get; set; } = string.Empty;
        public string IdentificacionComprador { get; set; } = string.Empty;
        public string SerieDocumentoModificado { get; set; } = string.Empty;
        public string NumeroDocumentoModificado { get; set; } = string.Empty;
        public bool EsFaltante { get; set; }
        public decimal Base { get; set; }
        public decimal Iva { get; set; }
    }

    public EDeclaraDeclaracionesService(
        IConfiguration configuration,
        EDeclaraComprasService comprasService)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
        _comprasService = comprasService;
    }

    public async Task<EDeclaraDeclaracionAnalisis> AnalizarAsync(int userId, int anio, int periodo)
    {
        await _comprasService.EnsureSchemaAsync();
        await EnsureSchemaAsync();
        ValidarPeriodo(anio, periodo);
        var identificacion = await ObtenerIdentificacionAsync(userId);

        await using var db = new SqlConnection(_connectionString);
        var documentos = (await db.QueryAsync<DocumentoAnalisis>("""
            SELECT d.TIPODOCUMENTO AS TipoDocumento,
                   ISNULL(NULLIF(c.CASILLAASOCIADA, ''), ISNULL(d.CASILLA, '')) AS Casilla,
                   d.TIPOFACTURACION AS TipoFacturacion,
                   d.SERIE AS Serie, d.NUMERO AS Numero, d.FECHAEMISION AS FechaEmision,
                   d.RUC AS Ruc, d.IDENTIFICACIONCOMPRADOR AS IdentificacionComprador,
                   d.SERIEDOCUMENTOMODIFICADO AS SerieDocumentoModificado,
                   d.NUMERODOCUMENTOMODIFICADO AS NumeroDocumentoModificado,
                   d.ESFALTANTE AS EsFaltante,
                   ISNULL(d.BASEIMPONIBLE, 0) AS Base,
                   ISNULL(d.IVA, 0) AS Iva
              FROM EDECLARA_COMPRA_DOCUMENTOS d
              LEFT JOIN EDECLARA_COMPRA_CASILLAS c ON c.ID=d.CASILLA AND c.ESTADO=1
             WHERE d.IDUSUARIO=@userId AND d.IDCONTRIBUYENTE=@userId
               AND d.ANIO=@anio AND d.PERIODO=@periodo AND d.TIPODOCUMENTO IN (1,2,3,4,6) AND d.ESTADO=1
             ORDER BY TRY_CONVERT(INT, d.CASILLA), d.ID
            """, new { userId, anio, periodo })).ToList();

        var compras = documentos.Where(x => x.TipoDocumento == 1).ToList();
        var ventas = documentos.Where(x => x.TipoDocumento == 2).ToList();
        var notas = documentos.Where(x => x.TipoDocumento == 3).ToList();
        var notasVentas = documentos.Where(x => x.TipoDocumento == 4).ToList();
        var retenciones = documentos.Where(x => x.TipoDocumento == 6).ToList();
        var pendientes = documentos.Count(x => x.TipoDocumento is (1 or 2 or 3 or 4) && !x.EsFaltante &&
            (x.TipoFacturacion is null or <= 0 || string.IsNullOrWhiteSpace(x.Casilla)));
        var notasSinFactura = notas.Count(x => !x.EsFaltante &&
            (string.IsNullOrWhiteSpace(x.SerieDocumentoModificado) || string.IsNullOrWhiteSpace(x.NumeroDocumentoModificado) ||
             !compras.Any(f => string.Equals(f.Serie, x.SerieDocumentoModificado, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(f.Numero, x.NumeroDocumentoModificado, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(f.Ruc, x.Ruc, StringComparison.OrdinalIgnoreCase))));
        var importesInvalidos = documentos.Any(x => x.Base < 0 || x.Iva < 0);
        var documentosFueraDePeriodo = documentos.Any(x => !PerteneceAlPeriodo(x.FechaEmision, anio, periodo));
        var identificacionInvalida = documentos.Any(x => !string.IsNullOrWhiteSpace(x.IdentificacionComprador) &&
            !CoincideIdentificacion(x.IdentificacionComprador, identificacion));
        var duplicados = documentos.Where(x => !x.EsFaltante)
            .GroupBy(x => new { x.TipoDocumento, x.Serie, x.Numero, x.Ruc, x.Iva })
            .Any(x => x.Count() > 1);
        var casillas = documentos
            .Where(x => x.TipoDocumento is 1 or 2 or 3 or 4)
            .GroupBy(x => new { Grupo = x.TipoDocumento is 2 or 4 ? "Ventas" : "Compras", Casilla = string.IsNullOrWhiteSpace(x.Casilla) ? "Sin casilla" : x.Casilla.Trim() })
            .Select(x => new EDeclaraCasillaAnalisis
            {
                Grupo = x.Key.Grupo,
                Casilla = x.Key.Casilla,
                Base = decimal.Round(x.Key.Grupo == "Ventas" ? x.Where(y => y.TipoDocumento == 2).Sum(y => y.Base) - x.Where(y => y.TipoDocumento == 4).Sum(y => y.Base) : x.Where(y => y.TipoDocumento == 1).Sum(y => y.Base) - x.Where(y => y.TipoDocumento == 3).Sum(y => y.Base), 2),
                Iva = decimal.Round(x.Key.Grupo == "Ventas" ? x.Where(y => y.TipoDocumento == 2).Sum(y => y.Iva) - x.Where(y => y.TipoDocumento == 4).Sum(y => y.Iva) : x.Where(y => y.TipoDocumento == 1).Sum(y => y.Iva) - x.Where(y => y.TipoDocumento == 3).Sum(y => y.Iva), 2),
                Documentos = x.Count()
            })
            .OrderBy(x => x.Casilla == "Sin casilla" ? 1 : 0)
            .ThenBy(x => x.Casilla)
            .ToList();

        var validaciones = new[]
        {
            Validacion("CUENTA", !string.IsNullOrWhiteSpace(identificacion), "La cuenta E-Declara tiene identificación configurada.", "Configura la identificación de la cuenta en Mi perfil de E-Declara.", true),
            Validacion("DOCUMENTOS", documentos.Count > 0, "El período tiene documentos descargados.", "No hay documentos descargados para el período seleccionado.", true),
            Validacion("PERIODO", !documentosFueraDePeriodo, "Las fechas corresponden al período seleccionado.", "Hay documentos cuya fecha no corresponde al período seleccionado.", true),
            Validacion("IDENTIFICACION", !identificacionInvalida, "Los documentos pertenecen a la identificación de la cuenta.", "Hay documentos que no pertenecen a la identificación de la cuenta.", true),
            Validacion("IMPORTES", !importesInvalidos, "Los importes tributarios son válidos.", "Hay documentos con importes tributarios negativos.", true),
            Validacion("DUPLICADOS", !duplicados, "No se encontraron documentos duplicados.", "Hay documentos duplicados dentro del período.", true),
            Validacion("NOTAS", notasSinFactura == 0, "Las notas de crédito tienen factura relacionada.", $"Hay {notasSinFactura} nota(s) de crédito sin factura relacionada.", true),
            Validacion("CLASIFICACION", pendientes == 0, "Todos los documentos están clasificados.", $"Hay {pendientes} documento(s) pendiente(s) de actividad o casilla.", true)
        };

        return new EDeclaraDeclaracionAnalisis
        {
            Anio = anio,
            Periodo = periodo,
            TotalDocumentos = documentos.Count,
            TotalFacturas = compras.Count,
            TotalNotasCredito = notas.Count,
            TotalRetenciones = retenciones.Count,
            PendientesClasificacion = pendientes,
            ComprasBase = decimal.Round(compras.Sum(x => x.Base), 2),
            ComprasIva = decimal.Round(compras.Sum(x => x.Iva), 2),
            NotasCreditoBase = decimal.Round(notas.Sum(x => x.Base), 2),
            NotasCreditoIva = decimal.Round(notas.Sum(x => x.Iva), 2),
            VentasBase = decimal.Round(ventas.Sum(x => x.Base), 2),
            VentasIva = decimal.Round(ventas.Sum(x => x.Iva), 2),
            NotasCreditoVentasBase = decimal.Round(notasVentas.Sum(x => x.Base), 2),
            NotasCreditoVentasIva = decimal.Round(notasVentas.Sum(x => x.Iva), 2),
            RetencionesIva = decimal.Round(retenciones.Sum(x => x.Iva), 2),
            Casillas = casillas,
            Validaciones = validaciones
        };
    }

    public async Task GuardarAsync(int userId, EDeclaraDeclaracionAnalisis analisis)
    {
        var calculado = await AnalizarAsync(userId, analisis.Anio, analisis.Periodo);
        if (!calculado.PuedeGuardarBorrador)
        {
            var bloqueos = calculado.Validaciones.Where(x => !x.EsValida && x.EsBloqueante).Select(x => x.Mensaje);
            throw new InvalidOperationException(string.Join(" ", bloqueos));
        }

        await using var db = new SqlConnection(_connectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();

        var id = await db.ExecuteScalarAsync<long?>("""
            SELECT ID FROM EDECLARA_DECLARACIONES
             WHERE IDUSUARIO=@userId AND ANIO=@Anio AND PERIODO=@Periodo AND ESTADO=1
            """, new { userId, Anio = calculado.Anio, Periodo = calculado.Periodo }, transaction);

        if (id is null)
        {
            id = await db.ExecuteScalarAsync<long>("""
                INSERT INTO EDECLARA_DECLARACIONES
                    (IDUSUARIO,ANIO,PERIODO,BASECOMPRAS,IVACOMPRAS,BASENOTASCREDITO,IVANOTASCREDITO,BASEVENTAS,IVAVENTAS,BASENOTASCREDVENTAS,IVANOTASCREDVENTAS,
                     RETENCIONESIVA,TOTALDOCUMENTOS,PENDIENTESCLASIFICACION,PUEDEGUARDAR,ESTADO,FECHACREACION,FECHAACTUALIZACION)
                OUTPUT INSERTED.ID
                VALUES (@userId,@Anio,@Periodo,@ComprasBase,@ComprasIva,@NotasCreditoBase,@NotasCreditoIva,@VentasBase,@VentasIva,@NotasCreditoVentasBase,@NotasCreditoVentasIva,
                        @RetencionesIva,@TotalDocumentos,@PendientesClasificacion,@PuedeGuardar,1,SYSUTCDATETIME(),SYSUTCDATETIME())
                """, new { userId, Anio = calculado.Anio, Periodo = calculado.Periodo,
                    ComprasBase = calculado.ComprasBase, ComprasIva = calculado.ComprasIva,
                    calculado.NotasCreditoBase, calculado.NotasCreditoIva, VentasBase = calculado.VentasBase, VentasIva = calculado.VentasIva,
                    NotasCreditoVentasBase = calculado.NotasCreditoVentasBase, NotasCreditoVentasIva = calculado.NotasCreditoVentasIva, calculado.RetencionesIva,
                    calculado.TotalDocumentos, calculado.PendientesClasificacion, PuedeGuardar = calculado.PuedeGuardarBorrador }, transaction);
        }
        else
        {
            await db.ExecuteAsync("""
                UPDATE EDECLARA_DECLARACIONES SET
                       BASECOMPRAS=@ComprasBase, IVACOMPRAS=@ComprasIva,
                       BASENOTASCREDITO=@NotasCreditoBase, IVANOTASCREDITO=@NotasCreditoIva,
                       BASEVENTAS=@VentasBase, IVAVENTAS=@VentasIva, BASENOTASCREDVENTAS=@NotasCreditoVentasBase, IVANOTASCREDVENTAS=@NotasCreditoVentasIva,
                       RETENCIONESIVA=@RetencionesIva, TOTALDOCUMENTOS=@TotalDocumentos,
                       PENDIENTESCLASIFICACION=@PendientesClasificacion, PUEDEGUARDAR=@PuedeGuardar,
                       FECHAACTUALIZACION=SYSUTCDATETIME()
                 WHERE ID=@id
                """, new { id, ComprasBase = calculado.ComprasBase, ComprasIva = calculado.ComprasIva,
                    calculado.NotasCreditoBase, calculado.NotasCreditoIva, VentasBase = calculado.VentasBase, VentasIva = calculado.VentasIva,
                    NotasCreditoVentasBase = calculado.NotasCreditoVentasBase, NotasCreditoVentasIva = calculado.NotasCreditoVentasIva, calculado.RetencionesIva,
                    calculado.TotalDocumentos, calculado.PendientesClasificacion, PuedeGuardar = calculado.PuedeGuardarBorrador }, transaction);
        }

        await db.ExecuteAsync("DELETE FROM EDECLARA_DECLARACION_CASILLAS WHERE IDDECLARACION=@id", new { id }, transaction);
        foreach (var casilla in calculado.Casillas)
        {
            await db.ExecuteAsync("""
                INSERT INTO EDECLARA_DECLARACION_CASILLAS (IDDECLARACION,TIPO,CASILLA,BASE,IVA,DOCUMENTOS)
                VALUES (@id,@Tipo,@Casilla,@Base,@Iva,@Documentos)
                """, new { id, Tipo = casilla.Grupo == "Ventas" ? 2 : 1, casilla.Casilla, casilla.Base, casilla.Iva, casilla.Documentos }, transaction);
        }

        await transaction.CommitAsync();
    }

    public async Task<(int Total, IReadOnlyList<EDeclaraDeclaracionResumen> Items)> ObtenerResumenAsync(int userId, int top = 10)
    {
        await EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        var total = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(1) FROM EDECLARA_DECLARACIONES
             WHERE IDUSUARIO=@userId AND ESTADO=1
            """, new { userId });
        var items = (await db.QueryAsync<EDeclaraDeclaracionResumen>("""
            SELECT TOP (@top)
                   CONCAT('Declaración IVA ', ANIO) AS Titulo,
                   CONCAT(ANIO, ' · período ', PERIODO) AS Periodo,
                   ESTADO AS Activo
              FROM EDECLARA_DECLARACIONES
             WHERE IDUSUARIO=@userId AND ESTADO=1
             ORDER BY FECHAACTUALIZACION DESC
            """, new { userId, top })).ToList();
        return (total, items);
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

            await using var db = new SqlConnection(_connectionString);
            await db.OpenAsync();
            await db.ExecuteAsync("""
                IF OBJECT_ID('dbo.EDECLARA_DECLARACIONES','U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EDECLARA_DECLARACIONES(
                        ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EDECLARA_DECLARACIONES PRIMARY KEY,
                        IDUSUARIO INT NOT NULL, ANIO INT NOT NULL, PERIODO INT NOT NULL,
                        BASECOMPRAS DECIMAL(18,2) NOT NULL DEFAULT 0, IVACOMPRAS DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BASENOTASCREDITO DECIMAL(18,2) NOT NULL DEFAULT 0, IVANOTASCREDITO DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BASEVENTAS DECIMAL(18,2) NOT NULL DEFAULT 0, IVAVENTAS DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BASENOTASCREDVENTAS DECIMAL(18,2) NOT NULL DEFAULT 0, IVANOTASCREDVENTAS DECIMAL(18,2) NOT NULL DEFAULT 0,
                        RETENCIONESIVA DECIMAL(18,2) NOT NULL DEFAULT 0, TOTALDOCUMENTOS INT NOT NULL DEFAULT 0,
                        PENDIENTESCLASIFICACION INT NOT NULL DEFAULT 0, PUEDEGUARDAR BIT NOT NULL DEFAULT 0,
                        ESTADO BIT NOT NULL DEFAULT 1,
                        FECHACREACION DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        FECHAACTUALIZACION DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UX_EDECLARA_DECLARACION_PERIODO UNIQUE(IDUSUARIO,ANIO,PERIODO)
                    );
                END;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','BASECOMPRAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD BASECOMPRAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_BASECOMPRAS DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','IVACOMPRAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD IVACOMPRAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_IVACOMPRAS DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','BASENOTASCREDITO') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD BASENOTASCREDITO DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_BASENC DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','IVANOTASCREDITO') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD IVANOTASCREDITO DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_IVANC DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','BASEVENTAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD BASEVENTAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_BASEVENTAS DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','IVAVENTAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD IVAVENTAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_IVAVENTAS DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','BASENOTASCREDVENTAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD BASENOTASCREDVENTAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_BASENCV DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','IVANOTASCREDVENTAS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD IVANOTASCREDVENTAS DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_IVANCV DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','RETENCIONESIVA') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD RETENCIONESIVA DECIMAL(18,2) NOT NULL CONSTRAINT DF_EDECLARA_DEC_RETIVA DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','TOTALDOCUMENTOS') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD TOTALDOCUMENTOS INT NOT NULL CONSTRAINT DF_EDECLARA_DEC_DOCS DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','PENDIENTESCLASIFICACION') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD PENDIENTESCLASIFICACION INT NOT NULL CONSTRAINT DF_EDECLARA_DEC_PEND DEFAULT 0;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACIONES','PUEDEGUARDAR') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACIONES ADD PUEDEGUARDAR BIT NOT NULL CONSTRAINT DF_EDECLARA_DEC_GUARDAR DEFAULT 0;
                IF OBJECT_ID('dbo.EDECLARA_DECLARACION_CASILLAS','U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EDECLARA_DECLARACION_CASILLAS(
                        ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EDECLARA_DECLARACION_CASILLAS PRIMARY KEY,
                        IDDECLARACION BIGINT NOT NULL, TIPO INT NOT NULL DEFAULT 1, CASILLA NVARCHAR(20) NOT NULL,
                        BASE DECIMAL(18,2) NOT NULL DEFAULT 0, IVA DECIMAL(18,2) NOT NULL DEFAULT 0,
                        DOCUMENTOS INT NOT NULL DEFAULT 0,
                        CONSTRAINT FK_EDECLARA_DECLARACION_CASILLAS FOREIGN KEY(IDDECLARACION) REFERENCES dbo.EDECLARA_DECLARACIONES(ID)
                    );
                END;
                IF COL_LENGTH('dbo.EDECLARA_DECLARACION_CASILLAS','TIPO') IS NULL ALTER TABLE dbo.EDECLARA_DECLARACION_CASILLAS ADD TIPO INT NOT NULL CONSTRAINT DF_EDECLARA_DEC_CAS_TIPO DEFAULT 1;
                """);
            _schemaEnsured = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private async Task<string> ObtenerIdentificacionAsync(int userId)
    {
        await using var db = new SqlConnection(_connectionString);
        var identificacion = await db.QuerySingleOrDefaultAsync<string>("""
            SELECT ISNULL(IDENTIFICACION,'') FROM Usuarios WHERE IdUsuario=@userId AND ESTADO=1
            """, new { userId });
        return SoloDigitos(identificacion);
    }

    private static EDeclaraDeclaracionValidacion Validacion(string codigo, bool valida, string ok, string error, bool bloqueante) => new()
    {
        Codigo = codigo,
        EsValida = valida,
        EsBloqueante = bloqueante,
        Mensaje = valida ? ok : error
    };

    private static bool PerteneceAlPeriodo(DateTime fecha, int anio, int periodo) =>
        fecha.Year == anio && periodo switch
        {
            13 => fecha.Month <= 6,
            14 => fecha.Month >= 7,
            >= 1 and <= 12 => fecha.Month == periodo,
            _ => false
        };

    private static void ValidarPeriodo(int anio, int periodo)
    {
        if (anio is < 2000 or > 2100 || periodo is < 1 or > 14)
            throw new InvalidOperationException("El período seleccionado no es válido.");

        var hoy = DateTime.Today;
        if (anio > hoy.Year || (anio == hoy.Year && (periodo <= 12 ? periodo > hoy.Month : periodo == 14 && hoy.Month < 7)))
            throw new InvalidOperationException("No se puede analizar un período fiscal futuro.");
    }

    private static string SoloDigitos(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool CoincideIdentificacion(string? a, string? b)
    {
        var x = SoloDigitos(a);
        var y = SoloDigitos(b);
        return x.Length >= 10 && y.Length >= 10 && x[..10] == y[..10];
    }
}
