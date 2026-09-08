using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models.EDeclara;

namespace Simetric.Services.EDeclara;

public sealed class EDeclaraComprasService
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static bool _schemaEnsured;
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EDeclaraSriCredentialService _credentialService;

    public EDeclaraComprasService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        EDeclaraSriCredentialService credentialService)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _credentialService = credentialService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
    }

    public async Task<IReadOnlyList<EDeclaraCompraDocumento>> ListarAsync(
        int userId, int contribuyenteId, int anio, int periodo, int tipoDocumento)
    {
        await EnsureSchemaAsync();
        await ValidarPerfilAsync(userId);
        contribuyenteId = userId;
        await using var db = new SqlConnection(_connectionString);
        const string sql = """
            SELECT d.ID AS Id, d.IDCONTRIBUYENTE AS IdContribuyente, d.ANIO AS Anio,
                   d.PERIODO AS Periodo, d.TIPODOCUMENTO AS TipoDocumento,
                   d.TIPOFACTURACION AS TipoFacturacion, d.SERIE AS Serie, d.NUMERO AS Numero,
                   d.FECHAEMISION AS FechaEmision, d.RUC AS Ruc, d.RAZONSOCIAL AS RazonSocial,
                   d.IDENTIFICACIONCOMPRADOR AS IdentificacionComprador, d.CONCEPTO AS Concepto,
                   d.CASILLA AS Casilla, d.TARIFA AS Tarifa, d.BASEIMPONIBLE AS BaseImponible,
                   d.IVA AS Iva, d.SERIEDOCUMENTOMODIFICADO AS SerieDocumentoModificado,
                   d.NUMERODOCUMENTOMODIFICADO AS NumeroDocumentoModificado,
                   d.NUMEROAUTORIZACION AS NumeroAutorizacion, d.ORIGEN AS Origen,
                   d.ESFALTANTE AS EsFaltante
              FROM EDECLARA_COMPRA_DOCUMENTOS d
             WHERE d.IDUSUARIO=@userId AND d.IDCONTRIBUYENTE=@contribuyenteId
               AND d.ANIO=@anio AND d.PERIODO=@periodo AND d.TIPODOCUMENTO=@tipoDocumento
               AND d.ESTADO=1
             ORDER BY d.RUC, d.FECHAEMISION, TRY_CONVERT(INT,d.NUMERO), d.TARIFA;
            """;
        var documentos = (await db.QueryAsync<EDeclaraCompraDocumento>(sql,
            new { userId, contribuyenteId, anio, periodo, tipoDocumento })).ToList();

        if (documentos.Count == 0)
            return documentos;

        var detalles = await db.QueryAsync<(long IdDocumento, string Detalle)>("""
            SELECT x.IDDOCUMENTO AS IdDocumento, x.DETALLE AS Detalle
              FROM EDECLARA_COMPRA_DETALLES x
             WHERE x.IDDOCUMENTO IN @ids
             ORDER BY x.ID
            """, new { ids = documentos.Select(x => x.Id).ToArray() });
        var porDocumento = detalles.GroupBy(x => x.IdDocumento).ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Select(y => y.Detalle).ToList());
        foreach (var documento in documentos)
            documento.Detalles = porDocumento.GetValueOrDefault(documento.Id) ?? Array.Empty<string>();
        return documentos;
    }

    public async Task<EDeclaraComprasCatalogos> ObtenerCatalogosAsync()
    {
        await EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        var actividades = (await db.QueryAsync<EDeclaraActividadCompra>("""
            SELECT ID AS Id, DESCRIPCION AS Descripcion FROM EDECLARA_COMPRA_ACTIVIDADES
             WHERE ESTADO=1 ORDER BY DESCRIPCION
            """)).ToList();
        var casillas = (await db.QueryAsync<(string Id, string Descripcion, bool Compras, bool NotasCredito, string CasillaAsociada)>("""
            SELECT ID AS Id, DESCRIPCION AS Descripcion, COMPRAS AS Compras, NOTASCREDITO AS NotasCredito,
                   CASILLAASOCIADA AS CasillaAsociada
              FROM EDECLARA_COMPRA_CASILLAS WHERE ESTADO=1 ORDER BY TRY_CONVERT(INT,ID), ID
            """)).ToList();
        var tarifas = (await db.QueryAsync<EDeclaraTarifaCompra>("""
            SELECT VALOR AS Valor, DESCRIPCION AS Descripcion FROM EDECLARA_COMPRA_TARIFAS
             WHERE ESTADO=1 ORDER BY VALOR
            """)).ToList();
        return new EDeclaraComprasCatalogos
        {
            Actividades = actividades,
            CasillasCompras = casillas.Where(x => x.Compras).Select(x => new EDeclaraCasillaCompra(x.Id, x.Descripcion, x.CasillaAsociada)).ToList(),
            CasillasNotasCredito = casillas.Where(x => x.NotasCredito).Select(x => new EDeclaraCasillaCompra(x.Id, x.Descripcion, x.CasillaAsociada)).ToList(),
            Tarifas = tarifas
        };
    }

    public async Task<long> GuardarAsync(int userId, EDeclaraCompraDocumento documento)
    {
        await EnsureSchemaAsync();
        var identificacion = await ValidarPerfilAsync(userId);
        documento.IdContribuyente = userId;
        ValidarDocumento(documento, identificacion);
        documento.Numero = NormalizarNumero(documento.Numero);
        documento.Serie = NormalizarSerie(documento.Serie);
        documento.NumeroDocumentoModificado = NormalizarNumeroOpcional(documento.NumeroDocumentoModificado);
        documento.SerieDocumentoModificado = NormalizarSerieOpcional(documento.SerieDocumentoModificado);
        if (documento.TipoDocumento == 3)
        {
            var factura = await BuscarFacturaRelacionadaAsync(userId, documento.IdContribuyente, documento.Anio, documento.Periodo,
                documento.SerieDocumentoModificado, documento.NumeroDocumentoModificado, documento.Ruc);
            if (factura is null) throw new InvalidOperationException("La factura relacionada no está registrada en compras para este período y proveedor.");
            documento.RazonSocial = string.IsNullOrWhiteSpace(documento.RazonSocial) ? factura.RazonSocial : documento.RazonSocial;
            documento.Concepto = string.IsNullOrWhiteSpace(documento.Concepto) ? factura.Concepto : documento.Concepto;
            documento.TipoFacturacion ??= factura.TipoFacturacion;
            if (string.IsNullOrWhiteSpace(documento.Casilla)) documento.Casilla = factura.Casilla;
        }

        await using var db = new SqlConnection(_connectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var duplicado = await db.ExecuteScalarAsync<int>("""
            SELECT COUNT(1) FROM EDECLARA_COMPRA_DOCUMENTOS
             WHERE IDUSUARIO=@userId AND IDCONTRIBUYENTE=@IdContribuyente AND TIPODOCUMENTO=@TipoDocumento
               AND SERIE=@Serie AND NUMERO=@Numero AND RUC=@Ruc AND TARIFA=@Tarifa AND ESTADO=1 AND ID<>@Id
            """, new { userId, documento.IdContribuyente, documento.TipoDocumento, documento.Serie, documento.Numero, documento.Ruc, documento.Tarifa, documento.Id }, transaction);
        if (duplicado > 0)
            throw new InvalidOperationException("El documento ya está registrado para la misma tarifa.");

        long id;
        if (documento.Id == 0)
        {
            id = await db.ExecuteScalarAsync<long>("""
                INSERT INTO EDECLARA_COMPRA_DOCUMENTOS
                    (IDUSUARIO,IDCONTRIBUYENTE,ANIO,PERIODO,TIPODOCUMENTO,TIPOFACTURACION,SERIE,NUMERO,
                     FECHAEMISION,RUC,RAZONSOCIAL,IDENTIFICACIONCOMPRADOR,CONCEPTO,CASILLA,TARIFA,BASEIMPONIBLE,
                     IVA,SERIEDOCUMENTOMODIFICADO,NUMERODOCUMENTOMODIFICADO,NUMEROAUTORIZACION,ORIGEN,
                     ESFALTANTE,ESTADO,IDUSUARIOINGRESO,FECHACREACION,FECHAACTUALIZACION)
                OUTPUT INSERTED.ID
                VALUES (@userId,@IdContribuyente,@Anio,@Periodo,@TipoDocumento,@TipoFacturacion,@Serie,@Numero,
                        @FechaEmision,@Ruc,@RazonSocial,@IdentificacionComprador,@Concepto,@Casilla,@Tarifa,@BaseImponible,
                        @Iva,@SerieDocumentoModificado,@NumeroDocumentoModificado,@NumeroAutorizacion,@Origen,
                        @EsFaltante,1,@userId,SYSUTCDATETIME(),SYSUTCDATETIME())
                """, new { userId, documento.IdContribuyente, documento.Anio, documento.Periodo, documento.TipoDocumento,
                    documento.TipoFacturacion, documento.Serie, documento.Numero, documento.FechaEmision, documento.Ruc,
                    documento.RazonSocial, documento.IdentificacionComprador, documento.Concepto, documento.Casilla,
                    documento.Tarifa, documento.BaseImponible, documento.Iva, documento.SerieDocumentoModificado,
                    documento.NumeroDocumentoModificado, documento.NumeroAutorizacion, documento.Origen, documento.EsFaltante }, transaction);
        }
        else
        {
            var actualizados = await db.ExecuteAsync("""
                UPDATE EDECLARA_COMPRA_DOCUMENTOS SET TIPOFACTURACION=@TipoFacturacion,SERIE=@Serie,NUMERO=@Numero,
                       FECHAEMISION=@FechaEmision,RUC=@Ruc,RAZONSOCIAL=@RazonSocial,CONCEPTO=@Concepto,CASILLA=@Casilla,
                       TARIFA=@Tarifa,BASEIMPONIBLE=@BaseImponible,IVA=@Iva,SERIEDOCUMENTOMODIFICADO=@SerieDocumentoModificado,
                       NUMERODOCUMENTOMODIFICADO=@NumeroDocumentoModificado,FECHAACTUALIZACION=SYSUTCDATETIME()
                 WHERE ID=@Id AND IDUSUARIO=@userId AND IDCONTRIBUYENTE=@IdContribuyente AND ESTADO=1
                """, new { userId, documento.Id, documento.IdContribuyente, documento.TipoFacturacion, documento.Serie,
                    documento.Numero, documento.FechaEmision, documento.Ruc, documento.RazonSocial, documento.Concepto,
                    documento.Casilla, documento.Tarifa, documento.BaseImponible, documento.Iva,
                    documento.SerieDocumentoModificado, documento.NumeroDocumentoModificado }, transaction);
            if (actualizados == 0)
                throw new InvalidOperationException("No se encontró el documento a actualizar.");
            id = documento.Id;
            await db.ExecuteAsync("DELETE FROM EDECLARA_COMPRA_DETALLES WHERE IDDOCUMENTO=@id", new { id }, transaction);
        }

        foreach (var detalle in documento.Detalles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            await db.ExecuteAsync("INSERT INTO EDECLARA_COMPRA_DETALLES (IDDOCUMENTO,DETALLE) VALUES (@id,@detalle)", new { id, detalle = detalle.Trim() }, transaction);
        await transaction.CommitAsync();
        return id;
    }

    public async Task ActualizarClasificacionAsync(int userId, long id, int? actividad, string? casilla)
    {
        await EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        var count = await db.ExecuteAsync("""
            UPDATE EDECLARA_COMPRA_DOCUMENTOS SET TIPOFACTURACION=@actividad,CASILLA=@casilla,
                   FECHAACTUALIZACION=SYSUTCDATETIME() WHERE ID=@id AND IDUSUARIO=@userId AND ESTADO=1
            """, new { userId, id, actividad, casilla = casilla?.Trim() ?? string.Empty });
        if (count == 0) throw new InvalidOperationException("No se encontró el documento.");
    }

    public async Task EliminarAsync(int userId, IEnumerable<long> ids)
    {
        await EnsureSchemaAsync();
        var values = ids.Distinct().ToArray();
        if (values.Length == 0) return;
        await using var db = new SqlConnection(_connectionString);
        await db.ExecuteAsync("""
            UPDATE EDECLARA_COMPRA_DOCUMENTOS SET ESTADO=0,IDUSUARIOELIMINA=@userId,
                   FECHAACTUALIZACION=SYSUTCDATETIME() WHERE IDUSUARIO=@userId AND ID IN @values
            """, new { userId, values });
    }

    public async Task<int> RegistrarFaltantesAsync(int userId, int contribuyenteId, int anio, int periodo, int tipoDocumento)
    {
        contribuyenteId = userId;
        var documentos = (await ListarAsync(userId, contribuyenteId, anio, periodo, tipoDocumento))
            .Where(x => !x.EsFaltante).GroupBy(x => x.Serie).ToList();
        var creados = 0;
        foreach (var grupo in documentos)
        {
            var existentes = grupo.Select(x => int.TryParse(x.Numero, out var n) ? n : 0).Where(x => x > 0).Distinct().Order().ToList();
            if (existentes.Count < 2) continue;
            var plantilla = grupo.First();
            for (var numero = existentes.First(); numero < existentes.Last(); numero++)
            {
                if (existentes.Contains(numero)) continue;
                var faltante = new EDeclaraCompraDocumento
                {
                    IdContribuyente=contribuyenteId,Anio=anio,Periodo=periodo,TipoDocumento=tipoDocumento,
                    Serie=grupo.Key,Numero=numero.ToString(CultureInfo.InvariantCulture),FechaEmision=plantilla.FechaEmision,
                    Concepto="Sin registros",Origen="SECUENCIA",EsFaltante=true
                };
                try { await GuardarAsync(userId, faltante); creados++; } catch (InvalidOperationException) { }
            }
        }
        return creados;
    }

    public async Task<EDeclaraImportacionResultado> ImportarXmlAsync(
        int userId, int contribuyenteId, int anio, int periodo, IEnumerable<(string Nombre, string Xml)> archivos, string origen = "XML")
    {
        var identificacion = await ValidarPerfilAsync(userId);
        contribuyenteId = userId;
        var importados = 0;
        var omitidos = 0;
        var errores = new List<string>();
        foreach (var archivo in archivos)
        {
            try
            {
                var registros = ParsearXml(archivo.Xml, contribuyenteId, anio, periodo, identificacion, origen);
                foreach (var registro in registros)
                {
                    try { await GuardarAsync(userId, registro); importados++; }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("ya está registrado", StringComparison.OrdinalIgnoreCase)) { omitidos++; }
                }
            }
            catch (Exception ex)
            {
                errores.Add($"{archivo.Nombre}: {ex.Message}");
            }
        }
        return new EDeclaraImportacionResultado(importados, omitidos, errores);
    }

    public async Task<EDeclaraImportacionResultado> ConsultarSriAsync(
        int userId, int contribuyenteId, int anio, int periodo, int tipoDocumento)
    {
        var clave = await _credentialService.ObtenerClaveAsync(userId);
        if (string.IsNullOrWhiteSpace(clave))
            throw new InvalidOperationException("Configura tu clave SRI en el perfil de E-Declara antes de consultar.");
        var ruc = await ValidarPerfilAsync(userId);
        contribuyenteId = userId;
        if (string.IsNullOrWhiteSpace(ruc)) throw new InvalidOperationException("La cuenta no tiene identificación configurada en Mi perfil.");
        var meses = periodo switch { 13 => Enumerable.Range(1, 6), 14 => Enumerable.Range(7, 6), >= 1 and <= 12 => new[] { periodo }, _ => throw new InvalidOperationException("Período inválido.") };
        var archivos = await DescargarXmlSriAsync(ruc, clave, anio, meses, tipoDocumento);
        var resultado = await ImportarXmlAsync(userId, contribuyenteId, anio, periodo, archivos, "SRI");
        return resultado;
    }

    public async Task<string?> BuscarRazonSocialAsync(int userId, string ruc)
    {
        await EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        return await db.QueryFirstOrDefaultAsync<string>("""
            SELECT TOP 1 RAZONSOCIAL FROM EDECLARA_COMPRA_DOCUMENTOS
             WHERE IDUSUARIO=@userId AND RUC=@ruc AND RAZONSOCIAL<>'' ORDER BY ID DESC
            """, new { userId, ruc = SoloDigitos(ruc) });
    }

    public async Task<EDeclaraCompraDocumento?> BuscarFacturaRelacionadaAsync(
        int userId, int contribuyenteId, int anio, int periodo, string serie, string numero, string ruc)
    {
        await EnsureSchemaAsync();
        contribuyenteId = userId;
        if (string.IsNullOrWhiteSpace(serie) || string.IsNullOrWhiteSpace(numero) || string.IsNullOrWhiteSpace(ruc)) return null;
        await using var db = new SqlConnection(_connectionString);
        return await db.QueryFirstOrDefaultAsync<EDeclaraCompraDocumento>("""
            SELECT TOP 1 d.ID AS Id, d.RAZONSOCIAL AS RazonSocial, d.CONCEPTO AS Concepto,
                   d.TIPOFACTURACION AS TipoFacturacion,
                   ISNULL(NULLIF(c.CASILLAASOCIADA,''),d.CASILLA) AS Casilla
              FROM EDECLARA_COMPRA_DOCUMENTOS d
              LEFT JOIN EDECLARA_COMPRA_CASILLAS c ON c.ID=d.CASILLA
             WHERE d.IDUSUARIO=@userId AND d.IDCONTRIBUYENTE=@contribuyenteId AND d.ANIO=@anio AND d.PERIODO=@periodo
               AND d.TIPODOCUMENTO=1 AND d.SERIE=@serie AND d.NUMERO=@numero AND d.RUC=@ruc AND d.ESTADO=1
             ORDER BY d.ID DESC
            """, new
        {
            userId,
            contribuyenteId,
            anio,
            periodo,
            serie = NormalizarSerie(serie),
            numero = NormalizarNumero(numero),
            ruc = SoloDigitos(ruc)
        });
    }

    private async Task<List<(string Nombre, string Xml)>> DescargarXmlSriAsync(
        string ruc, string clave, int anio, IEnumerable<int> meses, int tipoDocumento)
    {
        var endpoint = (_configuration["ApiDescargaComprobantes:BaseUrl"]
            ?? "http://68.178.204.190:8081/api/consultacomprobantes2/consultar").Trim();
        var periodos = meses.Distinct().OrderBy(x => x).ToList();
        var payload = new
        {
            Usuario = ruc,
            UsuarioAdicional = string.Empty,
            Password = clave,
            Dia = 0,
            Anio = anio.ToString(CultureInfo.InvariantCulture),
            Mes = periodos.First(),
            Comprobante = tipoDocumento,
            XMLpdf = true,
            Periodos = periodos.Select(mes => new { Anio = anio.ToString(CultureInfo.InvariantCulture), Mes = mes }).ToList()
        };

        var documentos = new List<(string Nombre, string Xml)>();
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);
        using var content = System.Net.Http.Json.JsonContent.Create(payload);
        using var response = await client.PostAsync(endpoint, content);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"El servicio SRI respondió HTTP {(int)response.StatusCode}.");

        var responseText = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseText)) return documentos;
        using var json = JsonDocument.Parse(responseText);
        if (!TryGetProperty(json.RootElement, "listaComprobantes", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return documentos;

        foreach (var item in lista.EnumerateArray())
        {
            var link = GetJsonString(item, "xmlLink");
            var xmlDirecto = GetJsonString(item, "xml");
            var xml = !string.IsNullOrWhiteSpace(xmlDirecto) ? xmlDirecto : await LeerXmlDesdeReferenciaAsync(client, endpoint, link);
            if (!string.IsNullOrWhiteSpace(xml)) documentos.Add(($"SRI-{anio}-{documentos.Count + 1}.xml", xml));
        }
        return documentos;
    }

    private static async Task<string?> LeerXmlDesdeReferenciaAsync(HttpClient client, string endpoint, string? referencia)
    {
        if (string.IsNullOrWhiteSpace(referencia)) return null;
        if (referencia.TrimStart().StartsWith('<')) return referencia;
        if (Uri.TryCreate(referencia, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return await client.GetStringAsync(uri);
        if (Path.GetExtension(referencia).Equals(".xml", StringComparison.OrdinalIgnoreCase) && File.Exists(referencia)) return await File.ReadAllTextAsync(referencia);
        if (Uri.TryCreate(new Uri(endpoint), referencia, out var relativa)) return await client.GetStringAsync(relativa);
        return null;
    }

    private static IReadOnlyList<EDeclaraCompraDocumento> ParsearXml(
        string xml, int contribuyenteId, int anio, int periodo, string identificacionEsperada, string origen)
    {
        if (string.IsNullOrWhiteSpace(xml)) throw new InvalidOperationException("El XML está vacío.");
        var envoltura = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var comprobanteTexto = envoltura.Descendants().FirstOrDefault(x => x.Name.LocalName == "comprobante")?.Value;
        var documento = !string.IsNullOrWhiteSpace(comprobanteTexto) ? XDocument.Parse(comprobanteTexto) : envoltura;
        var raiz = documento.Root?.DescendantsAndSelf().FirstOrDefault(x => x.Name.LocalName is "factura" or "notaCredito")
            ?? throw new InvalidOperationException("El XML no corresponde a una factura ni a una nota de crédito.");
        var tipo = raiz.Name.LocalName == "notaCredito" ? 3 : 1;
        string Valor(string nombre) => raiz.Descendants().FirstOrDefault(x => x.Name.LocalName == nombre)?.Value.Trim() ?? string.Empty;
        var fecha = ParseFecha(Valor("fechaEmision")) ?? throw new InvalidOperationException("El XML no contiene una fecha de emisión válida.");
        ValidarPeriodo(fecha, anio, periodo);
        var comprador = Valor("identificacionComprador");
        if (!CoincideIdentificacion(comprador, identificacionEsperada))
            throw new InvalidOperationException("El comprobante no pertenece a la identificación configurada en Mi perfil.");
        var serie = Valor("estab") + Valor("ptoEmi");
        var numero = Valor("secuencial");
        var ruc = Valor("ruc");
        var razon = Valor("razonSocial");
        var autorizacion = envoltura.Descendants().FirstOrDefault(x => x.Name.LocalName == "numeroAutorizacion")?.Value.Trim() ?? Valor("claveAcceso");
        var detalleNodos = raiz.Descendants().Where(x => x.Name.LocalName == "detalle").ToList();
        var detalles = detalleNodos.Select(x => x.Elements().FirstOrDefault(e => e.Name.LocalName == "descripcion")?.Value.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        var concepto = detalles.FirstOrDefault() ?? (tipo == 1 ? "Compra importada" : "Nota de crédito importada");
        var modificado = Valor("numDocModificado").Replace("-", string.Empty, StringComparison.Ordinal);
        var impuestos = raiz.Descendants().Where(x => x.Name.LocalName == "totalImpuesto")
            .Where(x => Child(x, "codigo") is "" or "2")
            .Select(x => new { Tarifa = ObtenerTarifa(x), Base = ParseDecimal(Child(x, "baseImponible")), Iva = ParseDecimal(Child(x, "valor")) })
            .Where(x => x.Base != 0 || x.Iva != 0 || x.Tarifa == 0).ToList();
        if (impuestos.Count == 0) impuestos.Add(new { Tarifa = 0m, Base = ParseDecimal(Valor("totalSinImpuestos")), Iva = 0m });
        return impuestos.Select(i => new EDeclaraCompraDocumento
        {
            IdContribuyente=contribuyenteId,Anio=anio,Periodo=periodo,TipoDocumento=tipo,Serie=serie,Numero=numero,
            FechaEmision=fecha,Ruc=ruc,RazonSocial=razon,IdentificacionComprador=comprador,Concepto=concepto,
            Tarifa=i.Tarifa,BaseImponible=i.Base,Iva=i.Iva,SerieDocumentoModificado=modificado.Length >= 6 ? modificado[..6] : string.Empty,
            NumeroDocumentoModificado=modificado.Length > 6 ? modificado[^Math.Min(9, modificado.Length)..] : string.Empty,
            NumeroAutorizacion=autorizacion,Origen=origen,Detalles=detalles
        }).ToList();
    }

    private async Task<string> ValidarPerfilAsync(int userId)
    {
        await EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        var identificacion = await db.QuerySingleOrDefaultAsync<string>("""
            SELECT ISNULL(IDENTIFICACION,'') FROM Usuarios WHERE IdUsuario=@userId AND ESTADO=1
            """, new { userId });
        if (string.IsNullOrWhiteSpace(identificacion))
            throw new InvalidOperationException("Configura tu identificación en Mi perfil de E-Declara antes de administrar compras.");
        return SoloDigitos(identificacion);
    }

    private static void ValidarDocumento(EDeclaraCompraDocumento d, string identificacionPerfil)
    {
        if (d.TipoDocumento is not (1 or 3)) throw new InvalidOperationException("Tipo de documento inválido.");
        if (string.IsNullOrWhiteSpace(d.Serie)) throw new InvalidOperationException("Ingresa la serie.");
        if (string.IsNullOrWhiteSpace(d.Numero)) throw new InvalidOperationException("Ingresa el número del documento.");
        if (d.FechaEmision == default) throw new InvalidOperationException("Ingresa la fecha de emisión.");
        ValidarPeriodo(d.FechaEmision, d.Anio, d.Periodo);
        if (!d.EsFaltante && string.IsNullOrWhiteSpace(d.Ruc)) throw new InvalidOperationException("Ingresa la identificación del proveedor.");
        if (!d.EsFaltante && !string.IsNullOrWhiteSpace(d.IdentificacionComprador) && !CoincideIdentificacion(d.IdentificacionComprador, identificacionPerfil))
            throw new InvalidOperationException("El documento no pertenece a la identificación configurada en Mi perfil.");
        if (d.BaseImponible < 0 || d.Iva < 0 || d.Tarifa < 0) throw new InvalidOperationException("Los valores tributarios no pueden ser negativos.");
    }

    private static void ValidarPeriodo(DateTime fecha, int anio, int periodo)
    {
        var valida = fecha.Year == anio && (periodo switch { 13 => fecha.Month <= 6, 14 => fecha.Month >= 7, >= 1 and <= 12 => fecha.Month == periodo, _ => false });
        if (!valida) throw new InvalidOperationException("La fecha de emisión no pertenece al período seleccionado.");
    }

    private async Task EnsureSchemaAsync()
    {
        if (_schemaEnsured) return;
        await SchemaLock.WaitAsync();
        try
        {
            if (_schemaEnsured) return;
            await using var db = new SqlConnection(_connectionString);
            await db.ExecuteAsync(SchemaSql);
            _schemaEnsured = true;
        }
        finally { SchemaLock.Release(); }
    }

    private const string SchemaSql = """
        IF OBJECT_ID('dbo.EDECLARA_COMPRA_DOCUMENTOS','U') IS NULL
        BEGIN
          CREATE TABLE dbo.EDECLARA_COMPRA_DOCUMENTOS(
            ID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EDECLARA_COMPRA_DOCUMENTOS PRIMARY KEY,
            IDUSUARIO INT NOT NULL, IDCONTRIBUYENTE INT NOT NULL, ANIO INT NOT NULL, PERIODO INT NOT NULL,
            TIPODOCUMENTO INT NOT NULL, TIPOFACTURACION INT NULL, SERIE NVARCHAR(10) NOT NULL, NUMERO NVARCHAR(20) NOT NULL,
            FECHAEMISION DATE NOT NULL, RUC NVARCHAR(20) NOT NULL DEFAULT '', RAZONSOCIAL NVARCHAR(300) NOT NULL DEFAULT '',
            IDENTIFICACIONCOMPRADOR NVARCHAR(20) NOT NULL DEFAULT '', CONCEPTO NVARCHAR(500) NOT NULL DEFAULT '',
            CASILLA NVARCHAR(20) NOT NULL DEFAULT '', TARIFA DECIMAL(7,2) NOT NULL DEFAULT 0,
            BASEIMPONIBLE DECIMAL(18,2) NOT NULL DEFAULT 0, IVA DECIMAL(18,2) NOT NULL DEFAULT 0,
            SERIEDOCUMENTOMODIFICADO NVARCHAR(10) NOT NULL DEFAULT '', NUMERODOCUMENTOMODIFICADO NVARCHAR(20) NOT NULL DEFAULT '',
            NUMEROAUTORIZACION NVARCHAR(100) NOT NULL DEFAULT '', ORIGEN NVARCHAR(20) NOT NULL DEFAULT 'MANUAL',
            ESFALTANTE BIT NOT NULL DEFAULT 0, ESTADO BIT NOT NULL DEFAULT 1, IDUSUARIOINGRESO INT NOT NULL,
            IDUSUARIOELIMINA INT NULL, FECHACREACION DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
            FECHAACTUALIZACION DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
          CREATE INDEX IX_EDECLARA_COMPRA_FILTRO ON dbo.EDECLARA_COMPRA_DOCUMENTOS(IDUSUARIO,IDCONTRIBUYENTE,ANIO,PERIODO,TIPODOCUMENTO,ESTADO);
        END;
        IF OBJECT_ID('dbo.EDECLARA_COMPRA_DETALLES','U') IS NULL
          CREATE TABLE dbo.EDECLARA_COMPRA_DETALLES(ID BIGINT IDENTITY(1,1) PRIMARY KEY,IDDOCUMENTO BIGINT NOT NULL,DETALLE NVARCHAR(1000) NOT NULL,
            CONSTRAINT FK_EDECLARA_COMPRA_DETALLE FOREIGN KEY(IDDOCUMENTO) REFERENCES dbo.EDECLARA_COMPRA_DOCUMENTOS(ID));
        IF OBJECT_ID('dbo.EDECLARA_COMPRA_ACTIVIDADES','U') IS NULL
        BEGIN
          CREATE TABLE dbo.EDECLARA_COMPRA_ACTIVIDADES(ID INT NOT NULL PRIMARY KEY,DESCRIPCION NVARCHAR(200) NOT NULL,ESTADO BIT NOT NULL DEFAULT 1);
          IF OBJECT_ID('dbo.GTACTIVIDADESPARAM','U') IS NOT NULL
            EXEC('INSERT INTO dbo.EDECLARA_COMPRA_ACTIVIDADES(ID,DESCRIPCION) SELECT idActividad,descripcion FROM dbo.GTACTIVIDADESPARAM WHERE estado=1 AND tipo IN (''C'',''T'',''I'')');
          IF NOT EXISTS(SELECT 1 FROM dbo.EDECLARA_COMPRA_ACTIVIDADES) INSERT INTO dbo.EDECLARA_COMPRA_ACTIVIDADES VALUES(1,'Actividad principal',1),(2,'Otra actividad',1);
        END;
        IF OBJECT_ID('dbo.EDECLARA_COMPRA_CASILLAS','U') IS NULL
        BEGIN
          CREATE TABLE dbo.EDECLARA_COMPRA_CASILLAS(ID NVARCHAR(20) NOT NULL PRIMARY KEY,DESCRIPCION NVARCHAR(300) NOT NULL,COMPRAS BIT NOT NULL DEFAULT 0,NOTASCREDITO BIT NOT NULL DEFAULT 0,CASILLAASOCIADA NVARCHAR(20) NOT NULL DEFAULT '',ESTADO BIT NOT NULL DEFAULT 1);
          IF OBJECT_ID('dbo.GTCASILLAS','U') IS NOT NULL
            EXEC('INSERT INTO dbo.EDECLARA_COMPRA_CASILLAS(ID,DESCRIPCION,COMPRAS,NOTASCREDITO,CASILLAASOCIADA) SELECT idCasilla,ISNULL(descripcion,''''),ISNULL(compras,0),ISNULL(notasCredCompras,0),ISNULL(casillaAsociada,'''') FROM dbo.GTCASILLAS');
          IF NOT EXISTS(SELECT 1 FROM dbo.EDECLARA_COMPRA_CASILLAS) INSERT INTO dbo.EDECLARA_COMPRA_CASILLAS VALUES
            ('500','Compras locales gravadas',1,0,'535',1),('510','Compras con tarifa diferente de cero',1,0,'535',1),('520','Compras con tarifa cero',1,0,'535',1),('530','Adquisiciones del período',1,0,'535',1),('535','Notas de crédito en compras',0,1,'',1);
        END;
        IF COL_LENGTH('dbo.EDECLARA_COMPRA_CASILLAS','CASILLAASOCIADA') IS NULL
          ALTER TABLE dbo.EDECLARA_COMPRA_CASILLAS ADD CASILLAASOCIADA NVARCHAR(20) NOT NULL CONSTRAINT DF_EDECLARA_COMPRA_CASILLA_ASOCIADA DEFAULT '';
        IF OBJECT_ID('dbo.EDECLARA_COMPRA_TARIFAS','U') IS NULL
        BEGIN
          CREATE TABLE dbo.EDECLARA_COMPRA_TARIFAS(VALOR DECIMAL(7,2) NOT NULL PRIMARY KEY,DESCRIPCION NVARCHAR(80) NOT NULL,ESTADO BIT NOT NULL DEFAULT 1);
          INSERT INTO dbo.EDECLARA_COMPRA_TARIFAS VALUES(0,'0%',1),(5,'5%',1),(8,'8%',1),(12,'12%',1),(13,'13%',1),(15,'15%',1),(20,'20%',1);
        END;
        """;

    private static string Child(XElement element, string name) => element.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
    private static decimal ObtenerTarifa(XElement impuesto)
    {
        var tarifa = ParseDecimal(Child(impuesto, "tarifa"));
        if (tarifa != 0) return tarifa;
        return Child(impuesto, "codigoPorcentaje") switch
        {
            "2" => 12m, "4" => 15m, "5" => 5m, "8" => 8m, "10" => 13m, "3610" => 20m, _ => 0m
        };
    }
    private static decimal ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0m;
    private static DateTime? ParseFecha(string? value) => DateTime.TryParseExact(value, new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
    private static string SoloDigitos(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static bool CoincideIdentificacion(string? a, string? b) { var x=SoloDigitos(a); var y=SoloDigitos(b); return x.Length >= 10 && y.Length >= 10 && x[..10] == y[..10]; }
    private static string NormalizarNumero(string value) => SoloDigitos(value).PadLeft(9, '0');
    private static string NormalizarNumeroOpcional(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizarNumero(value);
    private static string NormalizarSerie(string value) { var text=SoloDigitos(value); if (text.Length != 6) throw new InvalidOperationException("La serie debe tener 6 dígitos."); return text; }
    private static string NormalizarSerieOpcional(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : NormalizarSerie(value);
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value=property.Value; return true; }
        value=default; return false;
    }
    private static string? GetJsonString(JsonElement element, string name) => TryGetProperty(element,name,out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
