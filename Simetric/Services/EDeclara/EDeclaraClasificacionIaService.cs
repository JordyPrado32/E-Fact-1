using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using Simetric.Models.EDeclara;
using Simetric.Modules.AsistenteIAFacturacion.Config;

namespace Simetric.Services.EDeclara;

public sealed class EDeclaraClasificacionIaService
{
    private const int TamanoLote = 25;
    private const decimal ConfianzaMinima = 0.72m;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAISettings _settings;
    private readonly EDeclaraComprasService _comprasService;
    private readonly string _connectionString;

    public EDeclaraClasificacionIaService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        Microsoft.Extensions.Options.IOptions<OpenAISettings> settings,
        EDeclaraComprasService comprasService)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _comprasService = comprasService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection'.");
    }

    public async Task<EDeclaraClasificacionIaResultado> ClasificarAsync(int userId, int contribuyenteId, int anio, int periodo)
    {
        var catalogos = await _comprasService.ObtenerCatalogosAsync();
        var documentos = new List<EDeclaraCompraDocumento>();
        foreach (var tipoDocumento in new[] { 1, 2, 3, 4 })
            documentos.AddRange((await _comprasService.ListarAsync(userId, contribuyenteId, anio, periodo, tipoDocumento))
                .Where(x => x.RequiereClasificacion));

        if (documentos.Count == 0)
            return new(0, 0, 0, Array.Empty<string>());

        var apiKey = _configuration["OPENAI_API_KEY"] ?? _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return new(documentos.Count, 0, documentos.Count, ["Configura la API de OpenAI para automatizar la clasificación."]);

        var errores = new List<string>();
        var actualizaciones = new List<ActualizacionIa>();
        foreach (var lote in documentos.Chunk(TamanoLote))
        {
            try
            {
                var respuesta = await SolicitarClasificacionAsync(apiKey, lote, catalogos);
                ValidarRespuesta(respuesta, lote, catalogos, actualizaciones, errores);
            }
            catch (Exception ex)
            {
                errores.Add($"No se pudo clasificar un lote: {ex.Message}");
            }
        }

        var clasificados = await GuardarClasificacionesAsync(userId, contribuyenteId, anio, periodo, actualizaciones);
        return new(documentos.Count, clasificados, documentos.Count - clasificados, errores.Distinct().Take(8).ToList());
    }

    private async Task<RespuestaIa> SolicitarClasificacionAsync(
        string apiKey,
        IReadOnlyList<EDeclaraCompraDocumento> documentos,
        EDeclaraComprasCatalogos catalogos)
    {
        var payload = new
        {
            documentos = documentos.Select(x => new
            {
                id = x.Id,
                tipoDocumento = x.TipoDocumento switch { 2 => "venta", 3 or 4 => "nota_credito", _ => "compra" },
                tercero = x.RazonSocial,
                concepto = x.Concepto,
                detalles = x.Detalles.Take(8).ToArray(),
                tarifa = x.Tarifa,
                baseImponible = x.BaseImponible,
                iva = x.Iva,
                documentoRelacionado = x.TipoDocumento is 3 or 4 ? $"{x.SerieDocumentoModificado}-{x.NumeroDocumentoModificado}" : string.Empty
            }),
            actividades = catalogos.Actividades.Select(x => new { id = x.Id, descripcion = x.Descripcion }),
            casillas = new
            {
                compras = catalogos.CasillasCompras.Select(x => new { id = x.Id, descripcion = x.Descripcion, casillaDeclaracion = x.CasillaAsociada }),
                notasCredito = catalogos.CasillasNotasCredito.Select(x => new { id = x.Id, descripcion = x.Descripcion, casillaDeclaracion = x.CasillaAsociada }),
                ventas = catalogos.CasillasVentas.Select(x => new { id = x.Id, descripcion = x.Descripcion, casillaDeclaracion = x.CasillaAsociada }),
                notasCreditoVentas = catalogos.CasillasNotasCreditoVentas.Select(x => new { id = x.Id, descripcion = x.Descripcion, casillaDeclaracion = x.CasillaAsociada })
            }
        };

        var request = new
        {
            model = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4.1-mini" : _settings.Model,
            temperature = 0.1,
            max_completion_tokens = 3000,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "edeclara_clasificacion",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            items = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    additionalProperties = false,
                                    properties = new
                                    {
                                        id = new { type = "integer" },
                                        actividad = new { type = "integer" },
                                        casilla = new { type = "string" },
                                        confianza = new { type = "number" },
                                        motivo = new { type = "string" }
                                    },
                                    required = new[] { "id", "actividad", "casilla", "confianza", "motivo" }
                                }
                            }
                        },
                        required = new[] { "items" }
                    }
                }
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Eres un asistente tributario de Ecuador para E-Declara. Clasifica cada documento usando exclusivamente los IDs de actividad y casilla entregados. Usa compras para compras, notasCredito para notas de crédito de compras, ventas para ventas y notasCreditoVentas para notas de crédito de ventas. La casilla devuelta debe ser el ID del catálogo, no casillaDeclaracion. No inventes IDs. Si falta evidencia, usa actividad 0, casilla vacía y confianza 0. Devuelve una fila por documento y solo JSON válido."
                },
                new { role = "user", content = JsonSerializer.Serialize(payload) }
            }
        };

        var endpoint = (_configuration["OpenAI:BaseUrl"] ?? _settings.BaseUrl).TrimEnd('/') + "/chat/completions";
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI respondió HTTP {(int)response.StatusCode}.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var contentText = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(contentText))
            throw new InvalidOperationException("OpenAI no devolvió una clasificación.");
        return JsonSerializer.Deserialize<RespuestaIa>(contentText, JsonOptions)
            ?? throw new InvalidOperationException("La respuesta de OpenAI no tiene el formato esperado.");
    }

    private static void ValidarRespuesta(
        RespuestaIa respuesta,
        IReadOnlyList<EDeclaraCompraDocumento> documentos,
        EDeclaraComprasCatalogos catalogos,
        ICollection<ActualizacionIa> actualizaciones,
        ICollection<string> errores)
    {
        var porId = documentos.ToDictionary(x => x.Id);
        var usados = new HashSet<long>();
        foreach (var item in respuesta.Items)
        {
            if (!porId.TryGetValue(item.Id, out var documento) || !usados.Add(item.Id)) continue;
            if (item.Confianza < ConfianzaMinima || item.Actividad <= 0 || string.IsNullOrWhiteSpace(item.Casilla))
            {
                errores.Add($"El documento {documento.Serie}-{documento.Numero} quedó pendiente por baja confianza.");
                continue;
            }

            var casillas = documento.TipoDocumento switch
            {
                2 => catalogos.CasillasVentas,
                3 => catalogos.CasillasNotasCredito,
                4 => catalogos.CasillasNotasCreditoVentas,
                _ => catalogos.CasillasCompras
            };
            if (!catalogos.Actividades.Any(x => x.Id == item.Actividad) || !casillas.Any(x => x.Id == item.Casilla.Trim()))
            {
                errores.Add($"El documento {documento.Serie}-{documento.Numero} devolvió una actividad o casilla fuera del catálogo.");
                continue;
            }

            actualizaciones.Add(new(documento.Id, documento.TipoDocumento, item.Actividad, item.Casilla.Trim()));
        }
    }

    private async Task<int> GuardarClasificacionesAsync(int userId, int contribuyenteId, int anio, int periodo, IReadOnlyList<ActualizacionIa> actualizaciones)
    {
        if (actualizaciones.Count == 0) return 0;
        await _comprasService.EnsureSchemaAsync();
        await using var db = new SqlConnection(_connectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var clasificados = 0;
        foreach (var item in actualizaciones.DistinctBy(x => x.Id))
        {
            clasificados += await db.ExecuteAsync("""
                UPDATE EDECLARA_COMPRA_DOCUMENTOS
                   SET TIPOFACTURACION=@Actividad,CASILLA=@Casilla,ORIGEN='IA',FECHAACTUALIZACION=SYSUTCDATETIME()
                 WHERE ID=@Id AND IDUSUARIO=@userId AND IDCONTRIBUYENTE=@contribuyenteId AND ANIO=@anio AND PERIODO=@periodo
                   AND TIPODOCUMENTO=@TipoDocumento AND ESTADO=1
                   AND (TIPOFACTURACION IS NULL OR TIPOFACTURACION<=0 OR LTRIM(RTRIM(ISNULL(CASILLA,'')))='')
                """, new { item.Id, userId, contribuyenteId, anio, periodo, item.TipoDocumento, item.Actividad, item.Casilla }, transaction);
        }
        await transaction.CommitAsync();
        return clasificados;
    }

    private sealed record ActualizacionIa(long Id, int TipoDocumento, int Actividad, string Casilla);

    private sealed class RespuestaIa
    {
        [JsonPropertyName("items")]
        public List<ItemIa> Items { get; set; } = [];
    }

    private sealed class ItemIa
    {
        public long Id { get; set; }
        public int Actividad { get; set; }
        public string Casilla { get; set; } = string.Empty;
        public decimal Confianza { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }
}
