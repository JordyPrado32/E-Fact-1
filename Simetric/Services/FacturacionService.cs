using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System;
using System.Collections.Concurrent;
// Hola
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Simetric.Services
{
    public class FacturacionService
    {
        private const string MarcadorCompraDocumentosNotas = "[COMPRA_DOCS:";
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> FacturaSequenceLocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IEmailService _emailService;
        private readonly IFacturaPdfService _facturaPdfService;
        private readonly AuditService _auditService;
        private readonly ICajaSerieResolver _cajaSerieResolver;
        private readonly EmisionControlService _emisionControlService;
        private readonly InitialSequencePromptService _initialSequencePromptService;
        private readonly ComprobanteCorreoEstadoService _comprobanteCorreoEstadoService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly SriXmlProcessorService _sriXmlProcessorService;
        private readonly ILogger<FacturacionService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly FacturaStoredProcedureBootstrapService _facturaStoredProcedureBootstrapService;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly EmisorCertificadoProtector _certificadoProtector;
        public string? UltimoErrorGuardarFactura { get; private set; }

        public FacturacionService(
            IDbContextFactory<AppDbContext> dbFactory,
            IEmailService emailService,
            IFacturaPdfService facturaPdfService,
            AuditService auditService,
            ICajaSerieResolver cajaSerieResolver,
            EmisionControlService emisionControlService,
            InitialSequencePromptService initialSequencePromptService,
            ComprobanteCorreoEstadoService comprobanteCorreoEstadoService,
            IServiceScopeFactory serviceScopeFactory,
            SriXmlProcessorService sriXmlProcessorService,
            ILogger<FacturacionService> logger,
            IHttpContextAccessor httpContextAccessor,
            FacturaStoredProcedureBootstrapService facturaStoredProcedureBootstrapService,
            IWebHostEnvironment hostEnvironment,
            EmisorCertificadoProtector certificadoProtector)
        {
            _dbFactory = dbFactory;
            _emailService = emailService;
            _facturaPdfService = facturaPdfService;
            _auditService = auditService;
            _cajaSerieResolver = cajaSerieResolver;
            _emisionControlService = emisionControlService;
            _initialSequencePromptService = initialSequencePromptService;
            _comprobanteCorreoEstadoService = comprobanteCorreoEstadoService;
            _serviceScopeFactory = serviceScopeFactory;
            _sriXmlProcessorService = sriXmlProcessorService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _facturaStoredProcedureBootstrapService = facturaStoredProcedureBootstrapService;
            _hostEnvironment = hostEnvironment;
            _certificadoProtector = certificadoProtector;
        }

        private static object SnapshotCliente(Cliente c) => new
        {
            c.Codcliente,
            c.Numeroidentificacion,
            c.Nombres,
            c.Apellidos,
            c.Nombrerazonsocial,
            c.Nombrecomercial,
            c.Correo,
            c.Celular,
            c.Direccion,
            c.Estado,
            c.TipoCliente,
            c.Tipoidentificacion,
            c.Pais,
            c.Provincia,
            c.Ciudad,
            c.Observaciones
        };

        private static async Task NormalizarCamposClientePorTipoAsync(AppDbContext context, Cliente cliente)
        {
            cliente.Nombres = LimpiarTextoOpcional(cliente.Nombres);
            cliente.Apellidos = LimpiarTextoOpcional(cliente.Apellidos);
            cliente.Nombrerazonsocial = LimpiarTextoOpcional(cliente.Nombrerazonsocial);
            cliente.Nombrecomercial = LimpiarTextoOpcional(cliente.Nombrecomercial);

            var descripcionTipo = cliente.TipoCliente.HasValue
                ? await context.Tipoclientes
                    .AsNoTracking()
                    .Where(tipo => tipo.TclCodigo == cliente.TipoCliente.Value)
                    .Select(tipo => tipo.TclDescripcion)
                    .FirstOrDefaultAsync()
                : null;

            var esJuridica = TipoClienteClasificacion.EsJuridica(descripcionTipo);

            if (esJuridica)
            {
                cliente.Nombres = null;
                cliente.Apellidos = null;
                return;
            }

            cliente.Nombrerazonsocial = null;
            cliente.Nombrecomercial = null;
        }

        private static string? LimpiarTextoOpcional(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static object SnapshotFactura(Factura f) => new
        {
            f.Codfactura,
            f.Numfactura,
            f.Codclientes,
            f.Codemisor,
            f.Coddocumento,
            f.Fechaentrega,
            f.Subtotal12,
            f.Subtotal0,
            f.Subtotal,
            f.Descuentos,
            f.Iva,
            f.Valortotal,
            f.DescuentoGlobalPct,
            f.DescuentoGlobalValor,
            f.Serie,
            f.Guiaremision,
            f.Estado
        };

        private sealed class FacturaCorreoMetadata
        {
            public List<string> Destinatarios { get; set; } = new();
            public bool CorreoEnviado { get; set; }
            public DateTime? FechaEnvioCorreo { get; set; }
            public string? UltimoErrorCorreo { get; set; }
            public DateTime? SriFechaControlReintento { get; set; }
            public int SriIntentosDia { get; set; }
            public DateTime? SriUltimoIntentoAt { get; set; }
            public bool SriMostrarAlertaPendiente { get; set; }
        }

        private sealed class FacturaUsuarioContexto
        {
            public int IdUsuario { get; init; }
            public bool EsAsociado { get; init; }
            public int? IdJefe { get; init; }
            public string? Email { get; init; }

            public int IdUsuarioTitularCuenta =>
                EsAsociado && IdJefe is > 0
                    ? IdJefe.Value
                    : IdUsuario;
        }

        private static List<string> NormalizarCorreos(IEnumerable<string?>? correos)
        {
            if (correos == null)
                return new List<string>();

            return correos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ParseCorreosFactura(string? correoad)
        {
            if (string.IsNullOrWhiteSpace(correoad))
                return new List<string>();

            return NormalizarCorreos(
                correoad.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string SerializarCorreosFactura(IEnumerable<string> correos)
            => string.Join(";", NormalizarCorreos(correos));

        private static FacturaCorreoMetadata LeerFacturaCorreoMetadata(string? detalleextra)
        {
            if (string.IsNullOrWhiteSpace(detalleextra))
                return new FacturaCorreoMetadata();

            try
            {
                return JsonSerializer.Deserialize<FacturaCorreoMetadata>(detalleextra) ?? new FacturaCorreoMetadata();
            }
            catch
            {
                return new FacturaCorreoMetadata();
            }
        }

        private static string EscribirFacturaCorreoMetadata(FacturaCorreoMetadata metadata)
            => JsonSerializer.Serialize(metadata);

        private static FacturaCorreoMetadata NormalizarControlReintentoSri(FacturaCorreoMetadata metadata, DateTime fechaActual)
        {
            if (metadata.SriFechaControlReintento?.Date != fechaActual.Date)
            {
                metadata.SriFechaControlReintento = fechaActual.Date;
                metadata.SriIntentosDia = 0;
                metadata.SriMostrarAlertaPendiente = false;
            }

            return metadata;
        }

        private static string ObtenerNombreCliente(Cliente? cliente)
        {
            if (cliente == null)
                return "Cliente";

            if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
                return cliente.Nombrerazonsocial.Trim();

            var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
            if (!string.IsNullOrWhiteSpace(nombre))
                return nombre;

            return "Cliente";
        }

        private static string ObtenerNombreClienteFactura(Factura factura)
            => ObtenerNombreCliente(factura.CodclientesNavigation);

        private static string ObtenerNumeroFacturaDocumento(Factura factura)
        {
            var serie = factura.Serie?.Trim();
            var numero = factura.Numfactura?.Trim() ?? factura.Codfactura.ToString();

            return string.IsNullOrWhiteSpace(serie)
                ? numero
                : $"{serie}-{numero}";
        }

        private static string NormalizarNumeroGuiaRemision(string? numeroGuia)
            => new string((numeroGuia ?? string.Empty).Where(char.IsDigit).ToArray());

        private static string? FormatearNumeroGuiaRemision(string? numeroGuia)
        {
            var limpio = NormalizarNumeroGuiaRemision(numeroGuia);
            if (string.IsNullOrWhiteSpace(limpio))
                return null;

            if (limpio.Length != 15)
                return numeroGuia?.Trim();

            return $"{limpio[..3]}-{limpio.Substring(3, 3)}-{limpio.Substring(6, 9)}";
        }

        #region CATÁLOGOS Y LOOKUPS

        public async Task<List<Emisor>> GetEmisoresActivosAsync(int idUsuario)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var contextoSesion = GetFacturaUsuarioContextoDesdeSesion(idUsuario);
            var idUsuarioEmisor = contextoSesion.IdUsuarioTitularCuenta;
            if (idUsuarioEmisor <= 0)
            {
                idUsuarioEmisor = (await GetFacturaUsuarioContextoAsync(context, idUsuario)).IdUsuarioTitularCuenta;
            }

            return await context.Emisores
                .AsNoTracking()
                .Where(e => e.Estado == true && e.IdUsuario == idUsuarioEmisor && !e.EsEmisorSistema)
                .OrderBy(e => e.RazonSocial)
                .ToListAsync();
        }

        public async Task<List<Porcentajeiva>> GetPorcentajesIvaCatalogoAsync(bool incluirInactivos = false)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var q = context.Porcentajeivas.AsNoTracking().AsQueryable();

            if (!incluirInactivos)
                q = q.Where(x => x.Estado == "A");

            return await q
                .OrderBy(x => x.Codigo)
                .ToListAsync();
        }

        public async Task<List<Tipocliente>> GetTiposClienteAsync()
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Tipoclientes
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Pais>> GetPaisesAsync()
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            await PaisCatalogoService.AsegurarCatalogoAsync(context);

            return await context.Paises
                .AsNoTracking()
                .OrderBy(p => p.Descripcion)
                .ToListAsync();
        }

        public async Task<List<Provincia>> GetProvinciasByPaisAsync(int idPais)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Provincias
                .AsNoTracking()
                .Where(p => p.IdPais == idPais)
                .OrderBy(p => p.Descripcion)
                .ToListAsync();
        }

        public async Task<List<Ciudad>> GetCiudadesByProvinciaAsync(int idProvincia)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Ciudades
                .AsNoTracking()
                .Where(p => p.IdProvincia == idProvincia)
                .OrderBy(p => p.Descripcion)
                .ToListAsync();
        }

        #endregion

        #region GESTIÓN DE CLIENTES

        public async Task<Cliente?> GetClienteByIdentificacionAsync(int idUsuario, string identificacion)
        {
            if (string.IsNullOrWhiteSpace(identificacion))
                return null;

            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Usuario == idUsuario &&
                    c.Numeroidentificacion == identificacion.Trim() &&
                    (c.Estado == null || c.Estado == true));
        }

        public async Task<List<Cliente>> BuscarClientesFiltroAsync(int idUsuario, string filtro)
        {
            filtro = filtro.Trim().ToLowerInvariant();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var query = context.Clientes
                .AsNoTracking()
                .Where(c =>
                    c.Usuario == idUsuario &&
                    (c.Estado == null || c.Estado == true));

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                query = query.Where(c =>
                    (c.Numeroidentificacion ?? "").ToLower().Contains(filtro) ||
                    (c.Numcontribuyente ?? "").ToLower().Contains(filtro) ||
                    (c.Referencia ?? "").ToLower().Contains(filtro) ||
                    ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")).ToLower().Contains(filtro) ||
                    (c.Nombrerazonsocial ?? "").ToLower().Contains(filtro) ||
                    (c.Nombrecomercial ?? "").ToLower().Contains(filtro));
            }

            return await query
                .OrderBy(c => c.Nombrerazonsocial ?? c.Nombrecomercial ?? c.Nombres ?? c.Apellidos)
                .Take(15)
                .ToListAsync();
        }

        public async Task<List<string>> GetCorreosAdicionalesClienteAsync(int idUsuario, int codCliente)
        {
            if (idUsuario <= 0 || codCliente <= 0)
                return new List<string>();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var perteneceAlUsuario = await context.Clientes
                .AsNoTracking()
                .AnyAsync(c => c.Codcliente == codCliente && c.Usuario == idUsuario);

            if (!perteneceAlUsuario)
                return new List<string>();

            return await context.ClientesCorreos
                .AsNoTracking()
                .Where(cc => cc.CodCliente == codCliente && cc.Estado)
                .OrderBy(cc => cc.Id)
                .Select(cc => cc.Correo)
                .ToListAsync();
        }

        #endregion

        #region CAJA Y SECUENCIALES

        public async Task<Caja?> GetCajaUsuarioAsync(int idUsuario)
        {
            return await _cajaSerieResolver.ObtenerCajaAsync(idUsuario, tracking: false);
        }

        public async Task<string> GetSerieFacturaVisualAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieFacturaAsync(idUsuario);
            return resolucion.SerieVisual;
        }

        public async Task<string> GetSerieFacturaRawAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieFacturaAsync(idUsuario);
            return resolucion.SerieRaw;
        }

        public Task<List<CajaSerieResolucion>> GetSeriesFacturaHabilitadasAsync(int idUsuario)
        {
            return _cajaSerieResolver.ListarSeriesFacturaAsync(idUsuario);
        }

        public Task SavePreferredFacturaSeriesKeyAsync(int idUsuario, string? serieRaw)
        {
            return _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "factura", serieRaw);
        }

        public Task SavePreferredSeriesForAllDocumentsAsync(int idUsuario, string? serieRaw)
        {
            return Task.WhenAll(
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "factura", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "nota-credito", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "nota-debito", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "guia-remision", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "liquidacion-compra", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "compra-manual", serieRaw),
                _initialSequencePromptService.SavePreferredSeriesKeyAsync(idUsuario, "retencion", serieRaw));
        }

        public async Task<string> GetSerieNotaCreditoVisualAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieNotaCreditoAsync(idUsuario);
            return resolucion.SerieVisual;
        }

        public async Task<string> GetSerieNotaCreditoRawAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieNotaCreditoAsync(idUsuario);
            return resolucion.SerieRaw;
        }

        public async Task<string> GetSerieNotaDebitoVisualAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieNotaDebitoAsync(idUsuario);
            return resolucion.SerieVisual;
        }

        public async Task<string> GetSerieNotaDebitoRawAsync(int idUsuario)
        {
            var resolucion = await ResolverSerieNotaDebitoAsync(idUsuario);
            return resolucion.SerieRaw;
        }

        public async Task<string> GetNextFacturaNumeroAsync(int idUsuario, int? codEmisor = null, string? serieRaw = null)
        {
            var resolucion = await ResolverSerieFacturaAsync(idUsuario, serieRaw);
            await using var context = await _dbFactory.CreateDbContextAsync();
            var ultimoSecuencial = await ObtenerUltimoSecuencialFacturaAsync(
                context,
                idUsuario,
                resolucion.SerieRaw,
                codEmisor);

            var siguienteAutomatico = ultimoSecuencial > 0
                ? (ultimoSecuencial + 1).ToString("D9", CultureInfo.InvariantCulture)
                : string.Empty;

            var estadoSecuencia = await _initialSequencePromptService.GetStateAsync(
                idUsuario,
                "factura",
                resolucion.SerieRaw,
                codEmisor);

            var siguiente = _initialSequencePromptService.ResolveNextSequence(
                siguienteAutomatico,
                estadoSecuencia);

            return string.IsNullOrWhiteSpace(siguiente)
                ? string.Empty
                : siguiente;
        }

        public async Task<FacturaSecuenciaPendienteDto?> ObtenerFacturaPendienteSecuenciaAsync(
            int idUsuario,
            int? codEmisor,
            string? serieRaw)
        {
            if (idUsuario <= 0 || codEmisor is null or <= 0 || string.IsNullOrWhiteSpace(serieRaw))
                return null;

            await using var context = await _dbFactory.CreateDbContextAsync();
            return await ObtenerFacturaPendienteSecuenciaAsync(context, idUsuario, codEmisor, serieRaw);
        }

        private static async Task<FacturaSecuenciaPendienteDto?> ObtenerFacturaPendienteSecuenciaAsync(
            AppDbContext context,
            int idUsuario,
            int? codEmisor,
            string? serieRaw)
        {
            var serie = ExtractSerieDigits(serieRaw);
            if (serie.Length < 6)
                return null;

            var usuarios = await ObtenerUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario, codEmisor);
            var query = context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Coddocumento == 1 &&
                    f.Idusuario.HasValue &&
                    usuarios.Contains(f.Idusuario.Value) &&
                    (f.Serie ?? string.Empty).Replace("-", string.Empty) == serie);

            if (codEmisor is > 0)
                query = query.Where(f => f.Codemisor == codEmisor.Value);

            var ultima = await query
                .OrderByDescending(f => f.Codfactura)
                .Select(f => new
                {
                    f.Codfactura,
                    f.Serie,
                    f.Numfactura,
                    f.Estado,
                    f.Autorizado,
                    f.Estadoenviosri,
                    f.Mensaje,
                    f.Fchautorizacion,
                    f.Numautorizacion
                })
                .FirstOrDefaultAsync();

            if (ultima == null ||
                DocumentoAutorizacionHelper.EstaAutorizado(ultima.Autorizado, ultima.Estadoenviosri))
            {
                return null;
            }

            var tieneEvidenciaTransmision =
                ultima.Fchautorizacion.HasValue ||
                !string.IsNullOrWhiteSpace(ultima.Numautorizacion) ||
                !string.IsNullOrWhiteSpace(ultima.Estadoenviosri) ||
                !string.IsNullOrWhiteSpace(ultima.Mensaje);

            if (ultima.Estado == false && !tieneEvidenciaTransmision)
                return null;

            var destino = FacturaErrorCorreccionHelper.Clasificar(
                string.Join(" ", ultima.Estadoenviosri, ultima.Mensaje));

            return new FacturaSecuenciaPendienteDto
            {
                Codfactura = ultima.Codfactura,
                Serie = ultima.Serie ?? serie,
                Secuencial = ultima.Numfactura ?? string.Empty,
                EstadoSri = ultima.Estadoenviosri ?? DocumentoAutorizacionHelper.EstadoPendiente,
                MensajeSri = ultima.Mensaje ?? string.Empty,
                RutaCorreccion = FacturaErrorCorreccionHelper.ObtenerRuta(destino),
                EtiquetaCorreccion = FacturaErrorCorreccionHelper.ObtenerEtiqueta(destino)
            };
        }

        public async Task<bool> DebePreguntarSecuenciaInicialAsync(int idUsuario, int? codEmisor = null, string? serieRaw = null)
        {
            var resolucion = await ResolverSerieFacturaAsync(idUsuario, serieRaw);
            var estado = await _initialSequencePromptService.GetStateAsync(idUsuario, "factura", resolucion.SerieRaw, codEmisor);
            return estado.Initialized != true;
        }

        public async Task ConfigurarSecuenciaInicialFacturaAsync(int idUsuario, bool yaFacturoAntes, string? secuenciaAnterior, int? codEmisor = null, string? serieRaw = null)
        {
            var resolucion = await ResolverSerieFacturaAsync(idUsuario, serieRaw);
            var estadoActual = await _initialSequencePromptService.GetStateAsync(idUsuario, "factura", resolucion.SerieRaw, codEmisor);
            if (estadoActual.Initialized == true)
                return;

            string secuenciaNormalizada = string.Empty;

            if (yaFacturoAntes)
            {
                if (string.IsNullOrWhiteSpace(secuenciaAnterior))
                    throw new Exception("Debes ingresar la secuencia donde te quedaste.");

                if (!_initialSequencePromptService.TryNormalizeSequence(secuenciaAnterior, out secuenciaNormalizada))
                    throw new Exception("La secuencia ingresada no es válida.");
            }

            await _initialSequencePromptService.SaveStateAsync(
                idUsuario,
                "factura",
                resolucion.SerieRaw,
                new InitialSequencePromptState
                {
                    Initialized = true,
                    HadPreviousDocuments = yaFacturoAntes,
                    PreviousSequence = secuenciaNormalizada
                },
                codEmisor);
        }

        private async Task<CajaSerieResolucion> ResolverSerieFacturaAsync(int idUsuario, string? serieRaw = null)
        {
            if (!string.IsNullOrWhiteSpace(serieRaw))
            {
                return await _cajaSerieResolver.ResolverAsync(idUsuario, serieRaw);
            }

            var resolucionBase = await _cajaSerieResolver.ResolverAsync(idUsuario);
            var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
                idUsuario,
                "factura",
                resolucionBase.SerieRaw);

            if (!string.IsNullOrWhiteSpace(seriePreferida) &&
                !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
            {
                return await _cajaSerieResolver.ResolverAsync(idUsuario, seriePreferida);
            }

            return resolucionBase;
        }

        private async Task<CajaSerieResolucion> ResolverSerieNotaCreditoAsync(int idUsuario)
        {
            var resolucionBase = await _cajaSerieResolver.ResolverAsync(idUsuario);
            var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
                idUsuario,
                "nota-credito",
                resolucionBase.SerieRaw);

            if (!string.IsNullOrWhiteSpace(seriePreferida) &&
                !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
            {
                return await _cajaSerieResolver.ResolverAsync(idUsuario, seriePreferida);
            }

            return resolucionBase;
        }

        private async Task<CajaSerieResolucion> ResolverSerieNotaDebitoAsync(int idUsuario)
        {
            var resolucionBase = await _cajaSerieResolver.ResolverAsync(idUsuario);
            var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
                idUsuario,
                "nota-debito",
                resolucionBase.SerieRaw);

            if (!string.IsNullOrWhiteSpace(seriePreferida) &&
                !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
            {
                return await _cajaSerieResolver.ResolverAsync(idUsuario, seriePreferida);
            }

            return resolucionBase;
        }

        private static async Task<long> ObtenerUltimoSecuencialFacturaAsync(
            AppDbContext context,
            int idUsuario,
            string serieRaw,
            int? codEmisor = null)
        {
            var usuariosSincronizados = await ObtenerUsuariosSincronizadosPorEmisorRucAsync(context, idUsuario, codEmisor);
            if (usuariosSincronizados.Count == 0)
            {
                usuariosSincronizados.Add(idUsuario);
            }

            var query = context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario.HasValue &&
                    usuariosSincronizados.Contains(f.Idusuario.Value) &&
                    f.Serie == serieRaw);

            if (codEmisor is > 0)
            {
                query = query.Where(f => f.Codemisor == codEmisor.Value);
            }

            var ultimoNumero = await query
                .OrderByDescending(f => f.Codfactura)
                .Select(f => f.Numfactura)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(ultimoNumero))
            {
                var secuencial = new string(ultimoNumero.Where(char.IsDigit).ToArray());
                if (long.TryParse(secuencial, out var actual))
                    return actual;
            }

            IQueryable<Factura> fallbackQuery = context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario.HasValue &&
                    usuariosSincronizados.Contains(f.Idusuario.Value) &&
                    (f.Serie ?? string.Empty).Replace("-", string.Empty) == serieRaw);

            if (codEmisor.HasValue)
            {
                fallbackQuery = fallbackQuery.Where(f => f.Codemisor == codEmisor.Value);
            }

            var numeros = await fallbackQuery
                .Select(f => f.Numfactura)
                .ToListAsync();

            long max = 0;
            foreach (var numero in numeros)
            {
                var limpia = new string((numero ?? string.Empty).Where(char.IsDigit).ToArray());
                if (long.TryParse(limpia, out var actual) && actual > max)
                {
                    max = actual;
                }
            }

            return max;
        }

        #endregion

        #region BÚSQUEDA DE PRODUCTOS

        public async Task<List<ProductoLookupDetalleDto>> BuscarProductosFiltroAsync(int idUsuario, string filtro)
        {
            filtro = (filtro ?? string.Empty).Trim();
            var filtroLower = filtro.ToLowerInvariant();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var query = context.Productos
                .AsNoTracking()
                .Include(p => p.TipoProductoNavigation)
                .Include(p => p.IdsubtipoNavigation)
                .Where(p =>
                    (p.Estado == null || p.Estado == true) &&
                    p.Idusuario == idUsuario);

            if (!string.IsNullOrWhiteSpace(filtroLower))
            {
                query = query.Where(p =>
                    (p.Nombre ?? "").ToLower().Contains(filtroLower) ||
                    (p.CodigoPrincipal ?? "").ToLower().Contains(filtroLower) ||
                    (p.CodAuxiliar ?? "").ToLower().Contains(filtroLower) ||
                    (p.Tipocompravena ?? "").ToLower().Contains(filtroLower) ||
                    (p.TipoProductoNavigation != null && (p.TipoProductoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower)) ||
                    (p.IdsubtipoNavigation != null && (p.IdsubtipoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower)));
            }

            var productos = await query
                .OrderBy(p => p.Nombre)
                .Take(15)
                .ToListAsync();

            return productos.Select(MapearAProductoDto).ToList();
        }

        public async Task<ProductoLookupDetalleDto?> BuscarProductoParaDetalleAsync(int idUsuario, string codigoOTexto)
        {
            if (string.IsNullOrWhiteSpace(codigoOTexto))
                return null;

            var filtro = codigoOTexto.Trim();
            var filtroLower = filtro.ToLowerInvariant();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var p = await context.Productos
                .AsNoTracking()
                .Include(x => x.TipoProductoNavigation)
                .Include(x => x.IdsubtipoNavigation)
                .Where(x =>
                    (x.Estado == null || x.Estado == true) &&
                    x.Idusuario == idUsuario &&
                    (
                        x.CodigoPrincipal == filtro ||
                        x.CodAuxiliar == filtro ||
                        (x.Nombre ?? "").ToLower().Contains(filtroLower) ||
                        (x.Tipocompravena ?? "").ToLower().Contains(filtroLower) ||
                        (x.TipoProductoNavigation != null && (x.TipoProductoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower)) ||
                        (x.IdsubtipoNavigation != null && (x.IdsubtipoNavigation.Descripcion ?? "").ToLower().Contains(filtroLower))
                    ))
                .FirstOrDefaultAsync();

            if (p == null)
                return null;

            return MapearAProductoDto(p);
        }

        private ProductoLookupDetalleDto MapearAProductoDto(Producto p)
        {
            return new ProductoLookupDetalleDto
            {
                Codproducto = p.Codigo,
                Codprincipal = p.CodigoPrincipal,
                Codauxiliar = p.CodAuxiliar,
                Descripcion = p.Nombre,
                Categoria = p.TipoProductoNavigation?.Descripcion,
                Subcategoria = p.IdsubtipoNavigation?.Descripcion,
                PrecioUnitario = p.ValorUnitario ?? 0m,
                TipoProducto = p.TipoProducto ?? 0,
                SubtipoProducto = p.Idsubtipo ?? 0,
                CodigoImpuestoSri = p.Codigoimpuesto,
                TarifaIva = ObtenerTarifaIvaProducto(p)
            };
        }

        private static int ObtenerTarifaIvaProducto(Producto p)
        {
            var valor = TaxRateHelper.ParsePercentOrZero(p.Porcentajeimpuesto);
            var codigo = (int)Math.Round(valor, 0, MidpointRounding.AwayFromZero);

            return codigo switch
            {
                0 => 0,
                2 => 12,
                3 => 14,
                4 => 15,
                5 => 5,
                6 => 0,
                7 => 0,
                8 => 8,
                10 => 13,
                13 => 10,
                14 => 3,
                15 => 15,
                _ => Math.Max(0, TaxRateHelper.NormalizePercentInt(valor))
            };
        }

        public async Task<List<FacturaBusquedaDto>> BuscarFacturasAutocompleteAsync(string texto, int idUsuario)
        {
            texto = (texto ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(texto))
                return new List<FacturaBusquedaDto>();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var candidatos = await (
                from f in context.Facturas.AsNoTracking()
                join c in context.Clientes.AsNoTracking() on f.Codclientes equals c.Codcliente into cliJoin
                from c in cliJoin.DefaultIfEmpty()
                where f.Idusuario == idUsuario &&
                      f.Estado == true &&
                      f.Numfactura != null &&
                      f.Numfactura.Contains(texto)
                orderby f.Codfactura descending
                select new FacturaBusquedaDto
                {
                    Codfactura = f.Codfactura,
                    Numfactura = f.Numfactura ?? string.Empty,
                    Serie = f.Serie ?? string.Empty,
                    ClienteNombre = c != null
                        ? (!string.IsNullOrWhiteSpace(c.Nombrerazonsocial)
                            ? c.Nombrerazonsocial
                            : ((c.Nombres ?? string.Empty) + " " + (c.Apellidos ?? string.Empty)).Trim())
                        : string.Empty
                })
                .Take(40)
                .ToListAsync();

            return await FiltrarFacturasConSaldoDisponibleAsync(context, candidatos);
        }

        private static async Task<List<FacturaBusquedaDto>> FiltrarFacturasConSaldoDisponibleAsync(
            AppDbContext context,
            List<FacturaBusquedaDto> candidatos)
        {
            if (!candidatos.Any())
                return candidatos;

            var facturaIds = candidatos.Select(x => x.Codfactura).Distinct().ToList();

            var detallesFactura = await context.Detallefacturas
                .AsNoTracking()
                .Where(d => facturaIds.Contains(d.Codfactura))
                .Select(d => new
                {
                    d.Codfactura,
                    d.Codproducto,
                    Cantidad = d.Cantproducto,
                    Subtotal = d.Valortproducto
                })
                .ToListAsync();

            var anulados = await (
                from nc in context.NotaCreditos.AsNoTracking()
                join dnc in context.DetallesNotaCredito.AsNoTracking() on nc.Sec equals dnc.CodNotaCredito
                where nc.Estado == true &&
                      nc.IdDocModificado.HasValue &&
                      facturaIds.Contains(nc.IdDocModificado.Value)
                select new
                {
                    CodFactura = nc.IdDocModificado!.Value,
                    dnc.CodProducto,
                    Cantidad = dnc.CantProducto ?? 0m,
                    Subtotal = dnc.ValorTProducto ?? 0m
                })
                .ToListAsync();

            var facturasConSaldo = detallesFactura
                .GroupBy(d => d.Codfactura)
                .Where(grupo =>
                    grupo.Any(detalle =>
                    {
                        var anuladosProducto = anulados.Where(a =>
                            a.CodFactura == detalle.Codfactura &&
                            a.CodProducto == detalle.Codproducto);

                        var cantidadRestante = detalle.Cantidad - anuladosProducto.Sum(a => a.Cantidad);
                        var subtotalRestante = detalle.Subtotal - anuladosProducto.Sum(a => a.Subtotal);
                        return cantidadRestante > 0m && subtotalRestante > 0m;
                    }))
                .Select(g => g.Key)
                .ToHashSet();

            return candidatos
                .Where(x => facturasConSaldo.Contains(x.Codfactura))
                .Take(10)
                .ToList();
        }

        #endregion

        #region PROCESO DE GUARDADO (TRANSACCIONAL)

        public async Task<bool> GuardarFacturaCompletaAsync(
            int idUsuario,
            Factura factura,
            Cliente clienteData,
            List<Detallefactura> detalles,
            List<FacturaCorreoDestinoDto>? correosFactura = null)
        {
            UltimoErrorGuardarFactura = null;
            object? prevCliente = null;
            object? newCliente = null;
            var correosFacturaNormalizados = NormalizarCorreos(correosFactura?.Select(x => x.Correo));
            var correosGuardarEnCliente = NormalizarCorreos(
                correosFactura?
                    .Where(x => x.GuardarEnCliente)
                    .Select(x => x.Correo));

            await using var strategyContext = await _dbFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    await using var context = await _dbFactory.CreateDbContextAsync();
                    await _facturaStoredProcedureBootstrapService.EnsureSchemaAsync();
                    await using var transaction = await context.Database.BeginTransactionAsync();
                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        using var auditSuppression = SqlAuditScope.Suppress();

                        var usuarioContexto = GetFacturaUsuarioContextoDesdeSesion(idUsuario);
                        if (string.IsNullOrWhiteSpace(usuarioContexto.Email) || usuarioContexto.IdUsuarioTitularCuenta <= 0)
                        {
                            usuarioContexto = await GetFacturaUsuarioContextoAsync(context, idUsuario);
                        }
                        var idUsuarioEmisor = usuarioContexto.IdUsuarioTitularCuenta;

                        if (detalles == null || !detalles.Any())
                            throw new Exception("La factura debe contener al menos un ítem.");

                        if (factura.Codemisor <= 0)
                            throw new Exception("Debe seleccionar un emisor válido.");

                        var emisor = await context.Emisores
                            .AsNoTracking()
                            .Where(e =>
                                e.Codigo == factura.Codemisor &&
                                (e.EsEmisorSistema || e.IdUsuario == idUsuarioEmisor) &&
                                e.Estado)
                            .Select(e => new
                            {
                                e.Codigo,
                                e.EsEmisorSistema,
                                e.PathCertificado,
                                e.ClaveCertificado
                            })
                            .FirstOrDefaultAsync();

                        if (emisor is null)
                            throw new Exception("El emisor seleccionado no pertenece a tu cuenta principal o está inactivo.");

                        var resolucionFactura = emisor.EsEmisorSistema
                            ? await ResolverSerieFacturaSistemaAsync(context, factura.Serie)
                            : await ResolverSerieFacturaAsync(idUsuario, factura.Serie);

                        var caja = await context.Caja
                            .FirstOrDefaultAsync(c =>
                                c.Estado == true &&
                                c.Sec == resolucionFactura.CajaSec);

                        if (caja == null)
                            throw new Exception("No existe una caja activa para la serie seleccionada.");

                        var tieneFirmaConfigurada =
                            !string.IsNullOrWhiteSpace(emisor.PathCertificado) &&
                            !string.IsNullOrWhiteSpace(emisor.ClaveCertificado);

                        if (!tieneFirmaConfigurada && !emisor.EsEmisorSistema)
                        {
                            throw new Exception(EmisionControlService.MensajeFirmaRequerida);
                        }

                        var estadoEmision = emisor.EsEmisorSistema
                            ? new EmisionEstado
                            {
                                TieneEmisorActivo = true,
                                TieneFirmaElectronica = true,
                                PuedeEmitir = true,
                                EmisionPermitidaPorConfiguracion = true,
                                PlanIlimitadoActivo = true
                            }
                            : await _emisionControlService.ObtenerEstadoAsync(context, idUsuario);
                        if (!estadoEmision.PuedeEmitir)
                        {
                            throw new EmisionBloqueadaException(estadoEmision.Mensaje);
                        }

                        // La serie se toma de la seleccion activa y se valida contra las cajas habilitadas.
                        // El secuencial se recalcula desde esa serie al momento de guardar
                        // para no arrastrar un valor viejo que quedo cargado en pantalla.
                        factura.Serie = resolucionFactura.SerieRaw;
                        var sequenceLock = ObtenerFacturaSequenceLock(factura.Codemisor, factura.Serie);
                        await sequenceLock.WaitAsync();

                        try
                        {
                            await AdquirirBloqueoSqlSecuenciaFacturaAsync(context, factura.Codemisor, factura.Serie);

                            var facturaPendiente = await ObtenerFacturaPendienteSecuenciaAsync(
                                context,
                                idUsuario,
                                factura.Codemisor,
                                resolucionFactura.SerieRaw);
                            if (facturaPendiente != null)
                            {
                                throw new InvalidOperationException(
                                    $"La factura {facturaPendiente.NumeroCompleto} todavia no esta autorizada. " +
                                    "Debes corregirla o reenviarla antes de generar la siguiente secuencia.");
                            }

                            factura.Numfactura = await GetNextFacturaNumeroAsync(idUsuario, factura.Codemisor, resolucionFactura.SerieRaw);

                            await NormalizarCamposClientePorTipoAsync(context, clienteData);

                        var clienteDb = await context.Clientes
                            .FirstOrDefaultAsync(c =>
                                c.Usuario == idUsuario &&
                                c.Numeroidentificacion == clienteData.Numeroidentificacion);

                        if (clienteDb != null)
                        {
                            prevCliente = SnapshotCliente(clienteDb);

                            clienteDb.Nombres = clienteData.Nombres;
                            clienteDb.Apellidos = clienteData.Apellidos;
                            clienteDb.Nombrerazonsocial = clienteData.Nombrerazonsocial;
                            clienteDb.Nombrecomercial = clienteData.Nombrecomercial;
                            clienteDb.Correo = clienteData.Correo;
                            clienteDb.Celular = clienteData.Celular;
                            clienteDb.Telefonoconvencional = clienteData.Telefonoconvencional;
                            clienteDb.Direccion = clienteData.Direccion;
                            clienteDb.Observaciones = clienteData.Observaciones;
                            clienteDb.TipoCliente = clienteData.TipoCliente;
                            clienteDb.Tipoidentificacion = clienteData.Tipoidentificacion;
                            clienteDb.Pais = clienteData.Pais;
                            clienteDb.Provincia = clienteData.Provincia;
                            clienteDb.Ciudad = clienteData.Ciudad;
                            clienteDb.Usuario = idUsuario;
                            clienteDb.Estado = true;
                        }
                        else
                        {
                            clienteData.Fechaingreso = DateOnly.FromDateTime(DateTime.Now);
                            clienteData.Usuario = idUsuario;
                            clienteData.Estado = true;
                            clienteData.Codcliente = 0;

                            context.Clientes.Add(clienteData);
                            clienteDb = clienteData;
                        }

                        if (context.ChangeTracker.HasChanges())
                        {
                            await context.SaveChangesAsync();
                        }

                        newCliente = SnapshotCliente(clienteDb);

                        _logger.LogInformation(
                            "Factura stage cliente listo. Usuario {UsuarioId}. Tiempo acumulado: {ElapsedMs} ms",
                            idUsuario,
                            stopwatch.ElapsedMilliseconds);

                        var correoPrincipalCliente = (clienteDb.Correo ?? string.Empty).Trim();
                        var correosSincronizadosCliente = correosGuardarEnCliente
                            .Where(correo =>
                                !string.Equals(correo, correoPrincipalCliente, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        factura.Codclientes = clienteDb.Codcliente;
                        if (factura.Tiempocredito.HasValue && factura.Tiempocredito.Value <= 0)
                        {
                            factura.Tiempocredito = null;
                        }
                        else if ((factura.Tiempocredito == null || factura.Tiempocredito <= 0) &&
                                 factura.Tipopago == "19")
                        {
                            factura.Tiempocredito = clienteDb.DiasCredito;
                        }
                        factura.Coddocumento = (factura.Coddocumento <= 0) ? 1 : factura.Coddocumento;
                        factura.Fechaentrega ??= DateTime.Now;
                        factura.Estado = true;
                        factura.Idusuario = idUsuario;

                        await VincularProductosManualesAsync(context, idUsuarioEmisor, detalles);

                        if (factura.DescuentoGlobalPct == null)
                            factura.DescuentoGlobalPct = 0m;

                        if (factura.DescuentoGlobalPct > 1m)
                            factura.DescuentoGlobalPct = factura.DescuentoGlobalPct / 100m;

                        decimal dgPct = factura.DescuentoGlobalPct.Value;

                        decimal base12 = Math.Round(detalles.Where(d => d.Tarifa > 0).Sum(d => d.Valortproducto), 2);
                        decimal base0 = Math.Round(detalles.Where(d => d.Tarifa == 0).Sum(d => d.Valortproducto), 2);
                        decimal baseTotal = Math.Round(base12 + base0, 2);

                        decimal ivaAntes = Math.Round(detalles.Sum(d => d.Valoriva), 2);
                        decimal descuentoLineas = Math.Round(detalles.Sum(d => d.Descuento ?? 0m), 2);

                        decimal dgValor = Math.Round(baseTotal * dgPct, 2);
                        decimal dgBase12 = Math.Round(base12 * dgPct, 2);
                        decimal dgBase0 = Math.Round(base0 * dgPct, 2);

                        decimal dgIva = 0m;
                        if (base12 > 0m && ivaAntes > 0m)
                            dgIva = Math.Round(ivaAntes * (dgBase12 / base12), 2);

                        var destinatariosFactura = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(
                            new[] { clienteDb.Correo }
                                .Concat(correosSincronizadosCliente)
                                .Concat(correosFacturaNormalizados));

                        factura.DescuentoGlobalPct = dgPct;
                        factura.DescuentoGlobalValor = dgValor;

                        factura.Subtotal12 = Math.Round(base12 - dgBase12, 2);
                        factura.Subtotal0 = Math.Round(base0 - dgBase0, 2);
                        factura.Subtotal = Math.Round((factura.Subtotal12 ?? 0m) + (factura.Subtotal0 ?? 0m), 2);

                        factura.Descuentos = Math.Round(descuentoLineas + dgValor, 2);
                        factura.Iva = Math.Round(ivaAntes - dgIva, 2);
                        factura.Valortotal = Math.Round((factura.Subtotal ?? 0m) + (factura.Iva ?? 0m), 2);
                        factura.Correoad = SerializarCorreosFactura(destinatariosFactura);
                        factura.Detalleextra = EscribirFacturaCorreoMetadata(new FacturaCorreoMetadata
                        {
                            Destinatarios = destinatariosFactura,
                            CorreoEnviado = false,
                            FechaEnvioCorreo = null,
                            UltimoErrorCorreo = null
                        });

                        if (!string.IsNullOrWhiteSpace(factura.Guiaremision))
                        {
                            var guiaNormalizada = NormalizarNumeroGuiaRemision(factura.Guiaremision);
                            if (guiaNormalizada.Length != 15)
                                throw new Exception("La guia de remision debe tener el formato 001-001-000000001.");

                            factura.Guiaremision = FormatearNumeroGuiaRemision(factura.Guiaremision);
                        }
                        else
                        {
                            factura.Guiaremision = null;
                        }

                        // El payload de UI puede traer navegaciones hidratadas con otra instancia de Cliente.
                        // Dejamos solo las FK para evitar conflictos de tracking al adjuntar la factura.
                        factura.CodclientesNavigation = null;
                        factura.CodemisorNavigation = null;
                        factura.IdusuarioNavigation = null;
                        factura.CoddocumentoNavigation = null;

                        foreach (var d in detalles)
                        {
                            if (d.Cantproducto <= 0)
                                throw new Exception($"Cantidad inválida para producto ID {d.Codproducto}");

                            if (d.Cantproducto != decimal.Truncate(d.Cantproducto))
                                throw new Exception($"La cantidad del producto ID {d.Codproducto} debe ser un número entero.");

                            d.Valortproducto = Math.Round(d.Valortproducto, 2);
                            d.Valoriva = Math.Round(d.Valoriva, 2);
                            d.Valortotal = Math.Round(d.Valortotal, 2);
                        }

                        await _facturaStoredProcedureBootstrapService.EnsureSchemaAsync();

                        factura.Codfactura = await EjecutarGuardarFacturaStoredProcedureAsync(
                            context,
                            transaction,
                            usuarioContexto.IdUsuarioTitularCuenta,
                            estadoEmision.PlanIlimitadoActivo,
                            factura,
                            detalles);

                        foreach (var d in detalles)
                        {
                            d.Codfactura = factura.Codfactura;
                            d.Factura = factura;
                        }

                        factura.Detallefacturas = detalles;

                        var facturaNuevoSnapshot = SnapshotFactura(factura);

                        _logger.LogInformation(
                            "Factura stage stored procedure completado. Factura {FacturaId}. Tiempo acumulado: {ElapsedMs} ms",
                            factura.Codfactura,
                            stopwatch.ElapsedMilliseconds);

                        await transaction.CommitAsync();

                        _logger.LogInformation(
                            "Factura stage commit completado. Factura {FacturaId}. Tiempo total guardado: {ElapsedMs} ms",
                            factura.Codfactura,
                            stopwatch.ElapsedMilliseconds);

                        if (long.TryParse(new string((factura.Numfactura ?? string.Empty).Where(char.IsDigit).ToArray()), out var secActual))
                        {
                            await _initialSequencePromptService.UpdateLastSequenceAsync(
                                idUsuario,
                                "factura",
                                secActual.ToString(CultureInfo.InvariantCulture),
                                factura.Serie,
                                factura.Codemisor);
                        }

                            QueuePostCommitFacturaWork(
                                idUsuario,
                                factura.Codfactura,
                                factura.Numfactura,
                                factura.Codclientes,
                                factura.Subtotal12,
                                factura.Subtotal0,
                                factura.Iva,
                                factura.Valortotal,
                                detalles.Count,
                                clienteDb.Numeroidentificacion,
                                clienteDb.Correo,
                                correosSincronizadosCliente,
                                destinatariosFactura,
                                prevCliente,
                                newCliente,
                                facturaNuevoSnapshot,
                                factura.Serie,
                                factura.Codemisor);

                            return true;
                        }
                        finally
                        {
                            sequenceLock.Release();
                        }
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(); } catch { }
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                UltimoErrorGuardarFactura = ObtenerMensajeErrorGuardarFactura(ex);

                try
                {
                    var accionAuditoria = ClasificarErrorFacturaParaAuditoria(ex);
                    var exSql = ObtenerSqlException(ex);

                    await _auditService.TryRegistrarAuditoriaAsync(
                        idUsuario,
                        accionAuditoria,
                        prevCliente,
                        newCliente,
                        new
                        {
                            Mensaje = ex.Message,
                            TipoExcepcion = ex.GetType().FullName,
                            SqlNumber = exSql?.Number,
                            SqlError = exSql?.Message,
                            HResult = ex.HResult,
                            StackTrace = ex.StackTrace,
                            ClienteIdentificacion = clienteData?.Numeroidentificacion,
                            FacturaNumero = factura?.Numfactura,
                            FacturaSerie = factura?.Serie,
                            FacturaEmisor = factura?.Codemisor,
                            FacturaDocumento = factura?.Coddocumento,
                            Items = detalles?.Count ?? 0
                        });
                }
                catch { }

                _logger.LogError(ex,
                    "ERROR_GUARDAR_FACTURA {Numero} {Serie} {Cliente}",
                    factura?.Numfactura,
                    factura?.Serie,
                    clienteData?.Numeroidentificacion);
                return false;
            }
        }
        public async Task ActualizarAutorizacionFacturaAsync(int codFactura, string numeroAutorizacion, string fechaAutorizacion, string mensaje, Boolean autorizado)
        {
            // Buscamos la factura en la base de datos por su código único
            await using var context = await _dbFactory.CreateDbContextAsync();
            var facturaDb = await context.Facturas.FirstOrDefaultAsync(f => f.Codfactura == codFactura);

            if (facturaDb != null)
            {
                // Actualizamos los campos correspondientes
                facturaDb.Numautorizacion = numeroAutorizacion;
                facturaDb.Fechaautosri = fechaAutorizacion;
                facturaDb.Fchautorizacion = DateTime.Now;
                facturaDb.Autorizado = autorizado; // Aprovechamos para cambiar el estado a Autorizado
                facturaDb.Mensaje = mensaje;
                if (autorizado)
                    facturaDb.Estadoenviosri = DocumentoAutorizacionHelper.EstadoAutorizado;

                // Guardamos los cambios en la base de datos
                await context.SaveChangesAsync();

                if (autorizado)
                {
                    await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                        ComprobanteCorreoEstadoService.TipoFactura,
                        codFactura);

                    try
                    {
                        var facturaView = await GetFacturaCompletaAsync(codFactura);
                        if (facturaView?.Factura != null)
                        {
                            facturaView.Factura.Codclave = await ResolverClaveAccesoFacturaAsync(facturaView);
                            await _facturaPdfService.GenerarPdfFacturaAsync(facturaView, FormatoImpresionDocumento.A4);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo regenerar el PDF autorizado de la factura {FacturaId}.", codFactura);
                    }
                }
            }
            else
            {
                throw new Exception($"No se encontró la factura con el código {codFactura} para actualizar la autorización.");
            }
        }

        public async Task<List<int>> GetFacturasPendientesReintentoSriAsync(int maxRegistros = 10)
        {
            if (maxRegistros <= 0)
                return new List<int>();

            var ahora = DateTime.Now;
            var limite = ahora.AddHours(-24);
            await using var context = await _dbFactory.CreateDbContextAsync();

            var candidatas = await context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Coddocumento == 1 &&
                    f.Estado == true &&
                    f.Autorizado != true &&
                    f.Fchautorizacion.HasValue &&
                    f.Fchautorizacion >= limite)
                .Select(f => new
                {
                    f.Codfactura,
                    f.Mensaje,
                    f.Estadoenviosri,
                    f.Fchautorizacion,
                    f.Detalleextra
                })
                .ToListAsync();

            return candidatas
                .Where(f =>
                    !string.IsNullOrWhiteSpace(f.Mensaje) &&
                    DebeReintentarseEnvioSri(f.Mensaje) &&
                    !DocumentoAutorizacionHelper.EsNoAutorizado(f.Estadoenviosri) &&
                    NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(f.Detalleextra), ahora).SriIntentosDia < 3)
                .OrderBy(f => f.Fchautorizacion)
                .Select(f => f.Codfactura)
                .Take(maxRegistros)
                .ToList();
        }

        public async Task<List<int>> GetFacturasVencidasReintentoSriAsync(int maxRegistros = 10)
        {
            if (maxRegistros <= 0)
                return new List<int>();

            var limite = DateTime.Now.AddHours(-24);
            await using var context = await _dbFactory.CreateDbContextAsync();

            var candidatas = await context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Coddocumento == 1 &&
                    f.Estado == true &&
                    f.Autorizado != true &&
                    f.Fchautorizacion.HasValue &&
                    f.Fchautorizacion < limite)
                .Select(f => new
                {
                    f.Codfactura,
                    f.Mensaje,
                    f.Estadoenviosri,
                    f.Fchautorizacion
                })
                .ToListAsync();

            return candidatas
                .Where(f =>
                    !string.IsNullOrWhiteSpace(f.Mensaje) &&
                    DebeReintentarseEnvioSri(f.Mensaje) &&
                    !DocumentoAutorizacionHelper.EsNoAutorizado(f.Estadoenviosri))
                .OrderBy(f => f.Fchautorizacion)
                .Select(f => f.Codfactura)
                .Take(maxRegistros)
                .ToList();
        }

        public async Task<mensajeSRI> ReintentarEnvioSriFacturaAsync(int codFactura, bool esReintentoAutomatico = false)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var factura = await context.Facturas
                .Include(f => f.CodemisorNavigation)
                .FirstOrDefaultAsync(f => f.Codfactura == codFactura);

            if (factura == null)
            {
                return new mensajeSRI
                {
                    estado = "ERROR",
                    mensaje = "Factura no encontrada para reintento SRI."
                };
            }

            if (factura.Autorizado == true)
            {
                var metadataAutorizada = NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(factura.Detalleextra), DateTime.Now);
                metadataAutorizada.SriMostrarAlertaPendiente = false;
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataAutorizada);
                await context.SaveChangesAsync();

                return new mensajeSRI
                {
                    estado = DocumentoAutorizacionHelper.EstadoAutorizado,
                    mensaje = "La factura ya se encuentra autorizada."
                };
            }

            if (ContieneErrorSecuenciaSri(factura.Mensaje))
            {
                await MarcarFacturaSriRechazadaAsync(
                    codFactura,
                    DocumentoAutorizacionHelper.EstadoNoAutorizado,
                    string.IsNullOrWhiteSpace(factura.Mensaje)
                        ? "La factura no puede reenviarse al SRI por un error de secuencia."
                        : factura.Mensaje);

                return new mensajeSRI
                {
                    estado = DocumentoAutorizacionHelper.EstadoNoAutorizado,
                    mensaje = string.IsNullOrWhiteSpace(factura.Mensaje)
                        ? "La factura no puede reenviarse al SRI por un error de secuencia."
                        : factura.Mensaje
                };
            }

            if (ComprobanteReenvioFechaHelper.PuedeRenovarFecha(factura.Estadoenviosri, factura.Mensaje))
            {
                var fechaAnterior = factura.Fechaentrega ?? factura.Fchautorizacion ?? DateTime.Today;
                if (fechaAnterior.Date != DateTime.Today)
                    factura.Fechavence = ComprobanteReenvioFechaHelper.DesplazarFecha(factura.Fechavence, fechaAnterior);

                factura.Fechaentrega = DateTime.Today;
                factura.Fchautorizacion = DateTime.Today;
                factura.Codclave = null;
                factura.Numautorizacion = null;
                factura.Fechaautosri = null;
                await context.SaveChangesAsync();
            }

            var ahora = DateTime.Now;
            var metadataSri = NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(factura.Detalleextra), ahora);

            if (esReintentoAutomatico)
            {
                if (metadataSri.SriIntentosDia >= 3)
                {
                    metadataSri.SriMostrarAlertaPendiente = true;
                    factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataSri);
                    await context.SaveChangesAsync();

                    return new mensajeSRI
                    {
                        estado = "LIMITE_DIARIO",
                        mensaje = "La factura alcanzo el maximo de 3 reintentos automaticos del dia. Queda pendiente para reenvio manual."
                    };
                }

                metadataSri.SriIntentosDia++;
                metadataSri.SriUltimoIntentoAt = ahora;
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataSri);
                await context.SaveChangesAsync();
            }

            var emisor = await ResolverEmisorFacturaAsync(context, factura);
            if (emisor == null)
            {
                return new mensajeSRI
                {
                    estado = "ERROR",
                    mensaje = "No se encontro el emisor de la factura para reintentar el envio."
                };
            }

            factura.CodemisorNavigation = emisor;
            factura.Codemisor = emisor.Codigo;

            EliminarXmlFacturaGenerado(factura.Serie, factura.Numfactura);
            var rutaXml = await ProcesarXmlFacturaAsync(codFactura);
            var rutaCertificado = ResolverRutaFirmaElectronica(emisor.PathCertificado);
            var claveCertificado = ResolverClaveFirmaElectronica(emisor.ClaveCertificado);
            if (string.IsNullOrWhiteSpace(claveCertificado))
            {
                return new mensajeSRI
                {
                    estado = "ERROR",
                    mensaje = "El emisor configurado para esta factura no tiene una clave de firma electronica valida."
                };
            }

            var resultado = await _sriXmlProcessorService.ProcessXmlAsync(
                rutaXml,
                rutaCertificado,
                claveCertificado);

            if (string.Equals(resultado.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase))
            {
                await ActualizarAutorizacionFacturaAsync(
                    codFactura,
                    resultado.autorizacion ?? string.Empty,
                    resultado.fecha ?? DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "ok",
                    true);

                if (!string.IsNullOrWhiteSpace(resultado.xml))
                {
                    await GuardarXmlAutorizadoFacturaAsync(factura, resultado.xml);
                }

                var metadataProcesada = NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(factura.Detalleextra), ahora);
                metadataProcesada.SriIntentosDia = 0;
                metadataProcesada.SriMostrarAlertaPendiente = false;
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataProcesada);
                await context.SaveChangesAsync();

                try
                {
                    await AsegurarPdfFacturaAsync(codFactura);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo regenerar el PDF autorizado de la factura {FacturaId}.", codFactura);
                }
            }
            else if (ContieneErrorSecuenciaSri(resultado.mensaje) ||
                     ContieneErrorSecuenciaSri(resultado.xml))
            {
                await MarcarFacturaSriRechazadaAsync(
                    codFactura,
                    string.IsNullOrWhiteSpace(resultado.estado)
                        ? DocumentoAutorizacionHelper.EstadoNoAutorizado
                        : resultado.estado.Trim(),
                    string.IsNullOrWhiteSpace(resultado.mensaje)
                        ? "Factura rechazada por el SRI."
                        : resultado.mensaje);

                var metadataProcesada = NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(factura.Detalleextra), ahora);
                metadataProcesada.SriMostrarAlertaPendiente = false;
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataProcesada);
                await context.SaveChangesAsync();
            }
            else if (esReintentoAutomatico)
            {
                var metadataProcesada = NormalizarControlReintentoSri(LeerFacturaCorreoMetadata(factura.Detalleextra), ahora);
                metadataProcesada.SriMostrarAlertaPendiente = metadataProcesada.SriIntentosDia >= 3;
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadataProcesada);
                await context.SaveChangesAsync();
            }

            return resultado;
        }

        public async Task MarcarFacturaSriRechazadaAsync(int codFactura, string estadoSri, string mensaje)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var facturaDb = await context.Facturas.FirstOrDefaultAsync(f => f.Codfactura == codFactura);
            if (facturaDb == null)
                return;

            facturaDb.Autorizado = false;
            facturaDb.Estadoenviosri = string.IsNullOrWhiteSpace(estadoSri)
                ? DocumentoAutorizacionHelper.EstadoNoAutorizado
                : estadoSri.Trim();
            facturaDb.Mensaje = mensaje;
            facturaDb.Fchautorizacion = DateTime.Now;
            await context.SaveChangesAsync();
        }

        private static bool EsFalloTransitorioSri(string? mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje))
                return false;

            var texto = mensaje.Trim().ToUpperInvariant();
            return texto.Contains("TIMEOUT")
                || texto.Contains("TIEMPO DE ESPERA")
                || texto.Contains("ERROR INTERNO")
                || texto.Contains("RESPUESTA DE ERROR DE LA API")
                || texto.Contains("NO SE PUDO CONECTAR")
                || texto.Contains("CONEX")
                || texto.Contains("CONECT")
                || texto.Contains("SERVIDOR")
                || texto.Contains("HTTP 5")
                || texto.Contains("503")
                || texto.Contains("504");
        }

        private static bool ContieneErrorSecuenciaSri(string? mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje))
                return false;

            return mensaje.Contains("SECUENCIA", StringComparison.OrdinalIgnoreCase);
        }

        private static bool DebeReintentarseEnvioSri(string? mensaje)
        {
            if (string.IsNullOrWhiteSpace(mensaje))
                return false;

            return !ContieneErrorSecuenciaSri(mensaje);
        }

        private static bool EsFacturaBackOfficeSistema(Factura factura)
        {
            if (factura.CodemisorNavigation?.EsEmisorSistema == true)
                return true;

            return !string.IsNullOrWhiteSpace(factura.Notas) &&
                   factura.Notas.Contains(MarcadorCompraDocumentosNotas, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsFacturaBackOfficeSistema(FacturaViewDto facturaView)
        {
            if (facturaView.Emisor?.EsEmisorSistema == true)
                return true;

            return !string.IsNullOrWhiteSpace(facturaView.Factura?.Notas) &&
                   facturaView.Factura.Notas.Contains(MarcadorCompraDocumentosNotas, StringComparison.OrdinalIgnoreCase);
        }

        private static Emisor CrearEmisorFacturaView(Emisor emisor)
        {
            return new Emisor
            {
                Codigo = emisor.Codigo,
                Ruc = emisor.Ruc,
                RazonSocial = emisor.RazonSocial,
                NomComercial = emisor.NomComercial,
                DirEstablecimiento = emisor.DirEstablecimiento,
                CodEstablecimiento = emisor.CodEstablecimiento,
                CodPuntoEmision = emisor.CodPuntoEmision,
                DireccionMatriz = emisor.DireccionMatriz,
                Telefono = emisor.Telefono,
                LogoImagen = emisor.LogoImagen,
                TipoEmision = emisor.TipoEmision,
                TipoAmbiente = emisor.TipoAmbiente,
                EsEmisorSistema = emisor.EsEmisorSistema
            };
        }

        private static async Task<Emisor?> ObtenerEmisorSistemaActivoAsync(AppDbContext context)
        {
            return await context.Emisores
                .AsNoTracking()
                .Where(e => e.Estado && e.EsEmisorSistema)
                .OrderByDescending(e => e.Codigo)
                .FirstOrDefaultAsync();
        }

        private static async Task<Emisor?> ResolverEmisorFacturaAsync(AppDbContext context, Factura factura)
        {
            if (!EsFacturaBackOfficeSistema(factura))
                return factura.CodemisorNavigation;

            var emisorSistema = await ObtenerEmisorSistemaActivoAsync(context);
            if (emisorSistema == null)
                return factura.CodemisorNavigation;

            var emisorTrackeado = context.Emisores.Local.FirstOrDefault(e => e.Codigo == emisorSistema.Codigo);
            if (emisorTrackeado != null)
                return emisorTrackeado;

            if (factura.CodemisorNavigation?.Codigo == emisorSistema.Codigo)
                return factura.CodemisorNavigation;

            return emisorSistema;
        }

        private static async Task AplicarEmisorSistemaAFacturaViewAsync(AppDbContext context, FacturaViewDto? facturaView)
        {
            if (facturaView?.Factura == null || !EsFacturaBackOfficeSistema(facturaView))
                return;

            var emisorSistema = await ObtenerEmisorSistemaActivoAsync(context);
            if (emisorSistema == null)
                return;

            facturaView.Emisor = CrearEmisorFacturaView(emisorSistema);
            facturaView.Factura.Codemisor = emisorSistema.Codigo;
        }

        private string ResolverRutaFirmaElectronica(string? rutaFirma)
        {
            var rutaNormalizada = (rutaFirma ?? string.Empty).Trim().TrimStart('~', '/', '\\').Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(rutaNormalizada))
                throw new FileNotFoundException("No se encontro la firma electronica configurada.");

            var rutaOriginal = (rutaFirma ?? string.Empty).Trim().Replace('\\', '/');
            var nombreArchivo = Path.GetFileName(rutaNormalizada);
            var contentRoot = _hostEnvironment.ContentRootPath;
            var webRoot = string.IsNullOrWhiteSpace(_hostEnvironment.WebRootPath)
                ? Path.Combine(contentRoot, "wwwroot")
                : _hostEnvironment.WebRootPath;
            var candidatos = new List<string>();

            if (Path.IsPathRooted(rutaOriginal))
                candidatos.Add(rutaOriginal.Replace('/', Path.DirectorySeparatorChar));

            if (Path.IsPathRooted(rutaNormalizada))
                candidatos.Add(rutaNormalizada.Replace('/', Path.DirectorySeparatorChar));

            void AgregarCandidato(string baseDir, string relativePath)
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                    return;

                candidatos.Add(Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            AgregarCandidato(webRoot, $"App_Data/{rutaNormalizada}");
            AgregarCandidato(contentRoot, $"App_Data/{rutaNormalizada}");
            AgregarCandidato(contentRoot, rutaNormalizada);
            AgregarCandidato(webRoot, $"App_Data/certs/path/{nombreArchivo}");
            AgregarCandidato(contentRoot, $"App_Data/certs/path/{nombreArchivo}");
            AgregarCandidato(webRoot, $"App_Data/certs/system/{nombreArchivo}");
            AgregarCandidato(contentRoot, $"App_Data/certs/system/{nombreArchivo}");

            return candidatos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException($"No se encontro la firma electronica configurada: {rutaNormalizada}");
        }

        private string ResolverClaveFirmaElectronica(string? claveFirma)
        {
            var clave = _certificadoProtector.DesprotegerClave(claveFirma);
            return string.IsNullOrWhiteSpace(clave)
                ? (claveFirma ?? string.Empty).Trim()
                : clave.Trim();
        }

        private void QueuePostCommitFacturaWork(
            int idUsuario,
            int codFactura,
            string? numFactura,
            int? codCliente,
            decimal? subtotal12,
            decimal? subtotal0,
            decimal? iva,
            decimal? valorTotal,
            int totalItems,
            string? clienteIdentificacion,
            string? correoPrincipalCliente,
            IReadOnlyCollection<string> correosSincronizadosCliente,
            IReadOnlyCollection<string> destinatariosFactura,
            object? prevCliente,
            object? newCliente,
            object facturaNuevoSnapshot,
            string? serieFactura,
            int? codEmisor)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = _serviceScopeFactory.CreateAsyncScope();
                    var comprobanteCorreoEstadoService = scope.ServiceProvider.GetRequiredService<ComprobanteCorreoEstadoService>();
                    var initialSequencePromptService = scope.ServiceProvider.GetRequiredService<InitialSequencePromptService>();
                    var auditService = scope.ServiceProvider.GetRequiredService<AuditService>();

                    if (codFactura > 0 && destinatariosFactura.Count > 0)
                    {
                        await comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                            ComprobanteCorreoEstadoService.TipoFactura,
                            codFactura);
                    }

                    if (long.TryParse(new string((numFactura ?? string.Empty).Where(char.IsDigit).ToArray()), out var secActual))
                    {
                        await initialSequencePromptService.UpdateLastSequenceAsync(
                            idUsuario,
                            "factura",
                            secActual.ToString(CultureInfo.InvariantCulture),
                            serieFactura,
                            codEmisor);
                    }

                    if (codCliente is > 0)
                    {
                        await SincronizarCorreosClienteAsync(
                            scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                            codCliente.Value,
                            correoPrincipalCliente,
                            correosSincronizadosCliente);
                    }

                    await auditService.TryRegistrarAuditoriaAsync(
                        idUsuario,
                        "CLIENTE_UPSERT",
                        prevCliente,
                        newCliente,
                        new
                        {
                            Numeroidentificacion = clienteIdentificacion,
                            Nota = "Cliente creado/actualizado al guardar factura"
                        });

                    await auditService.TryRegistrarAuditoriaAsync(
                        idUsuario,
                        "FACTURA_CREADA",
                        null,
                        new
                        {
                            Codfactura = codFactura,
                            Numfactura = numFactura,
                            Codclientes = codCliente,
                            Subtotal12 = subtotal12,
                            Subtotal0 = subtotal0,
                            Iva = iva,
                            Valortotal = valorTotal,
                            Items = totalItems
                        },
                        new
                        {
                            ClienteIdentificacion = clienteIdentificacion,
                            Snapshot = facturaNuevoSnapshot
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "POST_COMMIT_FACTURA_FALLO. Factura {FacturaId}", codFactura);
                }
            });
        }

        private static async Task SincronizarCorreosClienteAsync(
            IDbContextFactory<AppDbContext> dbFactory,
            int codCliente,
            string? correoPrincipalCliente,
            IReadOnlyCollection<string> correosSincronizadosCliente)
        {
            await using var context = await dbFactory.CreateDbContextAsync();

            var correoPrincipalNormalizado = (correoPrincipalCliente ?? string.Empty).Trim();
            var correosDeseados = correosSincronizadosCliente
                .Where(correo => !string.Equals(correo, correoPrincipalNormalizado, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var registrosCorreosCliente = await context.ClientesCorreos
                .Where(cc => cc.CodCliente == codCliente)
                .ToListAsync();

            var correosDeseadosHash = new HashSet<string>(correosDeseados, StringComparer.OrdinalIgnoreCase);

            foreach (var registro in registrosCorreosCliente)
            {
                var correoNormalizado = (registro.Correo ?? string.Empty).Trim();
                var debePermanecerActivo =
                    !string.IsNullOrWhiteSpace(correoNormalizado) &&
                    correosDeseadosHash.Contains(correoNormalizado);

                if (!string.Equals(registro.Correo, correoNormalizado, StringComparison.Ordinal))
                    registro.Correo = correoNormalizado;

                if (registro.Estado != debePermanecerActivo)
                    registro.Estado = debePermanecerActivo;
            }

            var correosExistentesHash = new HashSet<string>(
                registrosCorreosCliente
                    .Where(x => !string.IsNullOrWhiteSpace(x.Correo))
                    .Select(x => x.Correo.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var correo in correosDeseados)
            {
                if (correosExistentesHash.Contains(correo))
                    continue;

                context.ClientesCorreos.Add(new ClienteCorreo
                {
                    CodCliente = codCliente,
                    Correo = correo,
                    Estado = true
                });
            }

            if (context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync();
        }

        private static async Task<FacturaUsuarioContexto> GetFacturaUsuarioContextoAsync(AppDbContext context, int idUsuario)
        {
            if (idUsuario <= 0)
            {
                return new FacturaUsuarioContexto { IdUsuario = idUsuario };
            }

            return await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => new FacturaUsuarioContexto
                {
                    IdUsuario = u.IdUsuario,
                    EsAsociado = u.estadoAsociado == true,
                    IdJefe = u.idJefe,
                    Email = u.Email
                })
                .FirstOrDefaultAsync()
                ?? new FacturaUsuarioContexto { IdUsuario = idUsuario };
        }

        private static async Task<List<int>> ObtenerUsuariosCuentaIdsAsync(AppDbContext context, int idUsuario)
        {
            var usuarioContexto = await GetFacturaUsuarioContextoAsync(context, idUsuario);
            var titularId = usuarioContexto.IdUsuarioTitularCuenta;

            return await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
                .Select(u => u.IdUsuario)
                .ToListAsync();
        }

        private static async Task<List<int>> ObtenerUsuariosSincronizadosPorEmisorRucAsync(AppDbContext context, int idUsuario, int? codEmisor = null)
        {
            var usuarios = await ObtenerUsuariosCuentaIdsAsync(context, idUsuario);
            if (!usuarios.Contains(idUsuario))
            {
                usuarios.Add(idUsuario);
            }

            IQueryable<Emisor> query = context.Emisores
                .AsNoTracking()
                .Where(e =>
                    e.Estado &&
                    !e.EsEmisorSistema &&
                    e.IdUsuario.HasValue &&
                    e.Ruc != null &&
                    e.Ruc != string.Empty);

            if (codEmisor is > 0)
            {
                query = query.Where(e => e.Codigo == codEmisor.Value || usuarios.Contains(e.IdUsuario.Value));
            }
            else
            {
                query = query.Where(e => usuarios.Contains(e.IdUsuario.Value));
            }

            var rucs = await query
                .Select(e => e.Ruc!.Trim())
                .Distinct()
                .ToListAsync();

            if (rucs.Count == 0)
            {
                return usuarios.Distinct().ToList();
            }

            var usuariosPorRuc = await context.Emisores
                .AsNoTracking()
                .Where(e =>
                    e.Estado &&
                    !e.EsEmisorSistema &&
                    e.IdUsuario.HasValue &&
                    e.Ruc != null &&
                    rucs.Contains(e.Ruc.Trim()))
                .Select(e => e.IdUsuario!.Value)
                .Distinct()
                .ToListAsync();

            return usuarios
                .Concat(usuariosPorRuc)
                .Distinct()
                .ToList();
        }

        private FacturaUsuarioContexto GetFacturaUsuarioContextoDesdeSesion(int idUsuario)
        {
            if (idUsuario <= 0)
                return new FacturaUsuarioContexto { IdUsuario = idUsuario };

            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return new FacturaUsuarioContexto { IdUsuario = idUsuario };

            var claimId = user.FindFirst("IdUsuario")?.Value;
            if (!int.TryParse(claimId, out var idUsuarioClaim) || idUsuarioClaim != idUsuario)
                return new FacturaUsuarioContexto { IdUsuario = idUsuario };

            var esAsociado = bool.TryParse(user.FindFirst("EstadoAsociado")?.Value, out var asociado) && asociado;
            var idJefe = int.TryParse(user.FindFirst("IdJefe")?.Value, out var jefe) ? jefe : (int?)null;

            return new FacturaUsuarioContexto
            {
                IdUsuario = idUsuario,
                EsAsociado = esAsociado,
                IdJefe = idJefe,
                Email = user.FindFirst(ClaimTypes.Email)?.Value
            };
        }

        private static string BuildFacturaSerieRaw(Caja caja)
        {
            var numeroCaja = caja.NumCaja ?? 0;
            if (numeroCaja <= 0)
                throw new Exception("La caja asignada no tiene un numero valido.");

            var establecimiento = ExtractSerieEstablecimiento(caja.SerieFactura);
            var puntoEmision = ExtractSeriePuntoEmision(caja.SerieFactura, numeroCaja);
            return $"{establecimiento}{puntoEmision}";
        }

        private static string ExtractSerieEstablecimiento(string? serie)
        {
            var digitos = ExtractSerieDigits(serie);
            return digitos.Length >= 3 ? digitos[..3] : "001";
        }

        private static string ExtractSeriePuntoEmision(string? serie, int numeroCaja)
        {
            var digitos = ExtractSerieDigits(serie);
            if (digitos.Length >= 6)
                return digitos.Substring(3, 3);

            return numeroCaja.ToString("D3");
        }

        private static string ExtractSerieDigits(string? serie)
            => new string((serie ?? string.Empty).Where(char.IsDigit).ToArray());

        private static SemaphoreSlim ObtenerFacturaSequenceLock(int? codEmisor, string? serieRaw)
        {
            var lockKey = $"{codEmisor.GetValueOrDefault()}:{ExtractSerieDigits(serieRaw)}";
            return FacturaSequenceLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        }

        private static Task AdquirirBloqueoSqlSecuenciaFacturaAsync(
            AppDbContext context,
            int? codEmisor,
            string? serieRaw)
        {
            var recurso = $"FACTURA_SECUENCIA:{codEmisor.GetValueOrDefault()}:{ExtractSerieDigits(serieRaw)}";
            return context.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @resultado INT;
EXEC @resultado = sys.sp_getapplock
    @Resource = {recurso},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @resultado < 0
    THROW 51000, 'No se pudo reservar la secuencia de factura. Intenta nuevamente.', 1;");
        }

        private static async Task<CajaSerieResolucion> ResolverSerieFacturaSistemaAsync(AppDbContext context, string? serieRaw = null)
        {
            var seriePreferida = ExtractSerieDigits(serieRaw);
            if (seriePreferida.Length >= 6)
                seriePreferida = seriePreferida[..6];

            var query = context.Caja
                .AsNoTracking()
                .Where(c => c.Estado == true && c.EsCajaSistema == true);

            Caja? caja = null;
            if (!string.IsNullOrWhiteSpace(seriePreferida))
            {
                var cajasSistema = await query
                    .OrderBy(c => c.NumCaja)
                    .ThenBy(c => c.Sec)
                    .ToListAsync();

                caja = cajasSistema
                    .FirstOrDefault(c => ExtractSerieDigits(c.SerieFactura) == seriePreferida);
            }

            caja ??= await query
                .OrderBy(c => c.NumCaja)
                .ThenBy(c => c.Sec)
                .FirstOrDefaultAsync();

            if (caja == null)
                throw new Exception("No existe una caja maestra activa para el emisor del sistema.");

            var numeroCaja = caja.NumCaja ?? 0;
            if (numeroCaja <= 0)
                throw new Exception("La caja maestra del sistema no tiene un numero valido.");

            var establecimiento = ExtractSerieEstablecimiento(caja.SerieFactura);
            var puntoEmision = ExtractSeriePuntoEmision(caja.SerieFactura, numeroCaja);
            var serieVisual = $"{establecimiento}-{puntoEmision}";

            return new CajaSerieResolucion(
                IdUsuario: caja.IdUsuario ?? 0,
                CajaSec: caja.Sec,
                IdTitularCuenta: caja.IdUsuario ?? 0,
                NumeroCaja: numeroCaja,
                Establecimiento: establecimiento,
                PuntoEmision: puntoEmision,
                SerieVisual: serieVisual,
                SerieRaw: serieVisual.Replace("-", string.Empty));
        }

        private static async Task<int> EjecutarGuardarFacturaStoredProcedureAsync(
            AppDbContext context,
            IDbContextTransaction transaction,
            int idUsuarioSaldo,
            bool planIlimitadoActivo,
            Factura factura,
            IReadOnlyCollection<Detallefactura> detalles)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = FacturaStoredProcedureBootstrapService.GuardarProcName;
            command.CommandType = CommandType.StoredProcedure;
            command.Transaction = transaction.GetDbTransaction();
            command.CommandTimeout = 90;

            AddFacturaProcedureParameter(command, "@IdUsuarioSaldo", SqlDbType.Int, idUsuarioSaldo);
            AddFacturaProcedureParameter(command, "@PlanIlimitadoActivo", SqlDbType.Bit, planIlimitadoActivo);
            AddFacturaProcedureParameter(command, "@Codclave", SqlDbType.NVarChar, factura.Codclave, 255);
            AddFacturaProcedureParameter(command, "@Codclientes", SqlDbType.Int, factura.Codclientes);
            AddFacturaProcedureParameter(command, "@Codemisor", SqlDbType.Int, factura.Codemisor);
            AddFacturaProcedureParameter(command, "@Coddocumento", SqlDbType.Int, factura.Coddocumento);
            AddFacturaProcedureParameter(command, "@Fechaentrega", SqlDbType.DateTime, factura.Fechaentrega ?? DateTime.Now);
            AddFacturaProcedureParameter(command, "@Numfactura", SqlDbType.NVarChar, factura.Numfactura, 50);
            AddFacturaProcedureParameter(command, "@Serie", SqlDbType.NVarChar, factura.Serie, 20);
            AddFacturaProcedureParameter(command, "@Guiaremision", SqlDbType.NVarChar, factura.Guiaremision, 50);
            AddFacturaProcedureParameter(command, "@Subtotal12", SqlDbType.Decimal, factura.Subtotal12 ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Subtotal0", SqlDbType.Decimal, factura.Subtotal0 ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Subtotal", SqlDbType.Decimal, factura.Subtotal ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Descuentos", SqlDbType.Decimal, factura.Descuentos ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Iva", SqlDbType.Decimal, factura.Iva ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Valortotal", SqlDbType.Decimal, factura.Valortotal ?? 0m, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@DescuentoGlobalPct", SqlDbType.Decimal, factura.DescuentoGlobalPct, precision: 18, scale: 6);
            AddFacturaProcedureParameter(command, "@DescuentoGlobalValor", SqlDbType.Decimal, factura.DescuentoGlobalValor, precision: 18, scale: 2);
            AddFacturaProcedureParameter(command, "@Correoad", SqlDbType.NVarChar, factura.Correoad, -1);
            AddFacturaProcedureParameter(command, "@Detalleextra", SqlDbType.NVarChar, factura.Detalleextra, -1);
            AddFacturaProcedureParameter(command, "@Notas", SqlDbType.NVarChar, factura.Notas, -1);
            AddFacturaProcedureParameter(command, "@Estado", SqlDbType.Bit, factura.Estado ?? true);
            AddFacturaProcedureParameter(command, "@Idusuario", SqlDbType.Int, factura.Idusuario);
            AddFacturaProcedureParameter(command, "@Tipopago", SqlDbType.NVarChar, factura.Tipopago, 20);
            AddFacturaProcedureParameter(command, "@Tiempocredito", SqlDbType.Int, factura.Tiempocredito);
            AddFacturaProcedureParameter(command, "@Ambiente", SqlDbType.Int, factura.Ambiente);

            var detallesParameter = new SqlParameter("@Detalles", SqlDbType.Structured)
            {
                TypeName = FacturaStoredProcedureBootstrapService.DetalleTypeName,
                Value = BuildFacturaDetallesDataTable(detalles)
            };
            command.Parameters.Add(detallesParameter);

            var result = await command.ExecuteScalarAsync();
            if (result is null || result == DBNull.Value || !int.TryParse(result.ToString(), out var codFactura))
                throw new InvalidOperationException("No se pudo obtener el código de la factura insertada.");

            return codFactura;
        }

        private static void AddFacturaProcedureParameter(
            DbCommand command,
            string name,
            SqlDbType dbType,
            object? value,
            int? size = null,
            byte? precision = null,
            byte? scale = null)
        {
            var parameter = new SqlParameter(name, dbType)
            {
                Value = value ?? DBNull.Value
            };

            if (size.HasValue)
                parameter.Size = size.Value;

            if (precision.HasValue)
                parameter.Precision = precision.Value;

            if (scale.HasValue)
                parameter.Scale = scale.Value;

            command.Parameters.Add(parameter);
        }

        private static string ObtenerMensajeErrorGuardarFactura(Exception ex)
        {
            if (ex is OperationCanceledException)
                return "La factura tardó demasiado en procesarse. Intenta nuevamente.";

            if (ex is SqlException sqlEx && sqlEx.Number == -2)
                return "La factura superó el tiempo máximo de SQL. Intenta nuevamente.";

            if (ex.InnerException is SqlException innerSqlEx && innerSqlEx.Number == -2)
                return "La factura superó el tiempo máximo de SQL. Intenta nuevamente.";

            return ex.Message;
        }

        private static string ClasificarErrorFacturaParaAuditoria(Exception ex)
        {
            var mensaje = ex.Message ?? string.Empty;
            if (mensaje.Contains("autoriz", StringComparison.OrdinalIgnoreCase) ||
                mensaje.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                mensaje.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                mensaje.Contains("acceso", StringComparison.OrdinalIgnoreCase))
            {
                return "ERROR_GUARDAR_FACTURA_NO_AUTORIZADA";
            }

            if (ex is OperationCanceledException)
                return "ERROR_GUARDAR_FACTURA_CANCELADA";

            if (ex is SqlException sqlEx && sqlEx.Number == -2)
                return "ERROR_GUARDAR_FACTURA_TIMEOUT_SQL";

            if (ObtenerSqlException(ex)?.Number == -2)
                return "ERROR_GUARDAR_FACTURA_TIMEOUT_SQL";

            return "ERROR_GUARDAR_FACTURA";
        }

        private static SqlException? ObtenerSqlException(Exception ex)
        {
            if (ex is SqlException sqlEx)
                return sqlEx;

            return ex.InnerException as SqlException;
        }

        private static string NormalizarNombreProductoManual(string? nombre) =>
            (nombre ?? string.Empty).Trim().ToUpperInvariant();

        private static int ResolverTarifaDetalle(Detallefactura detalle)
        {
            if (detalle.Tarifa > 0)
                return detalle.Tarifa;

            if (detalle.Valortproducto > 0m && detalle.Valoriva > 0m)
            {
                var tarifaCalculada = Math.Round((detalle.Valoriva / detalle.Valortproducto) * 100m, 0, MidpointRounding.AwayFromZero);
                return Convert.ToInt32(tarifaCalculada, CultureInfo.InvariantCulture);
            }

            return 0;
        }

        private async Task VincularProductosManualesAsync(AppDbContext context, int ownerId, List<Detallefactura> detalles)
        {
            var detallesManuales = detalles
                .Where(d => d.Codproducto <= 0 && !string.IsNullOrWhiteSpace(d.Descripproducto))
                .ToList();

            if (!detallesManuales.Any())
                return;

            var nombresNormalizados = detallesManuales
                .Select(d => NormalizarNombreProductoManual(d.Descripproducto))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var productosExistentes = await context.Productos
                .Where(p =>
                    p.Idusuario == ownerId &&
                    p.Estado == true &&
                    p.Nombre != null &&
                    nombresNormalizados.Contains((p.Nombre ?? string.Empty).Trim().ToUpper()))
                .ToListAsync();

            var productosPorNombre = productosExistentes
                .GroupBy(p => NormalizarNombreProductoManual(p.Nombre))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Codigo).First(), StringComparer.Ordinal);

            var tarifasIva = await context.Porcentajeivas
                .AsNoTracking()
                .Where(x => x.Estado == "A" || x.Estado == "1")
                .Select(x => new { x.Codigo, x.Valor, x.ValorCalculo })
                .ToListAsync();

            string? ResolverCodigoIva(int tarifa)
            {
                if (tarifa <= 0)
                    return null;

                foreach (var iva in tarifasIva)
                {
                    if (decimal.TryParse(iva.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var valorIva) &&
                        decimal.Round(valorIva, 0, MidpointRounding.AwayFromZero) == tarifa)
                    {
                        return iva.Codigo;
                    }

                    if (iva.ValorCalculo.HasValue &&
                        decimal.Round(iva.ValorCalculo.Value * 100m, 0, MidpointRounding.AwayFromZero) == tarifa)
                    {
                        return iva.Codigo;
                    }
                }

                return null;
            }

            foreach (var detalle in detallesManuales)
            {
                var nombre = (detalle.Descripproducto ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                var clave = NormalizarNombreProductoManual(nombre);
                if (!productosPorNombre.TryGetValue(clave, out var producto))
                {
                    var tarifa = ResolverTarifaDetalle(detalle);
                    var codigoIva = ResolverCodigoIva(tarifa);

                    producto = new Producto
                    {
                        Nombre = nombre,
                        CodigoPrincipal = string.IsNullOrWhiteSpace(detalle.Codprincipal) ? null : detalle.Codprincipal.Trim(),
                        CodAuxiliar = string.IsNullOrWhiteSpace(detalle.Codauxiliar) ? null : detalle.Codauxiliar.Trim(),
                        ValorUnitario = Math.Round(detalle.Precioproducto, 2, MidpointRounding.AwayFromZero),
                        Estado = true,
                        Facturable = true,
                        Tipocompravena = "SERVICIO",
                        Codigoimpuesto = string.IsNullOrWhiteSpace(codigoIva) ? null : "2",
                        Porcentajeimpuesto = codigoIva,
                        Idusuario = ownerId
                    };

                    context.Productos.Add(producto);
                    await context.SaveChangesAsync();
                    productosPorNombre[clave] = producto;
                }

                detalle.Codproducto = producto.Codigo;
                detalle.Codprincipal = string.IsNullOrWhiteSpace(detalle.Codprincipal) ? producto.CodigoPrincipal : detalle.Codprincipal;
                detalle.Codauxiliar = string.IsNullOrWhiteSpace(detalle.Codauxiliar) ? producto.CodAuxiliar : detalle.Codauxiliar;
            }
        }

        private static DataTable BuildFacturaDetallesDataTable(IEnumerable<Detallefactura> detalles)
        {
            var table = new DataTable();
            table.Columns.Add("Codproducto", typeof(int));
            table.Columns.Add("Codprincipal", typeof(string));
            table.Columns.Add("Codauxiliar", typeof(string));
            table.Columns.Add("Cantproducto", typeof(decimal));
            table.Columns.Add("Descripproducto", typeof(string));
            table.Columns.Add("Precioproducto", typeof(decimal));
            table.Columns.Add("Descuento", typeof(decimal));
            table.Columns.Add("Valortproducto", typeof(decimal));
            table.Columns.Add("Valoriva", typeof(decimal));
            table.Columns.Add("Valortotal", typeof(decimal));
            table.Columns.Add("Tarifa", typeof(int));
            table.Columns.Add("Valorice", typeof(decimal));
            table.Columns.Add("Costo", typeof(decimal));
            table.Columns.Add("Bonificacion", typeof(int));

            foreach (var detalle in detalles)
            {
                table.Rows.Add(
                    detalle.Codproducto,
                    (object?)detalle.Codprincipal ?? DBNull.Value,
                    (object?)detalle.Codauxiliar ?? DBNull.Value,
                    detalle.Cantproducto,
                    (object?)detalle.Descripproducto ?? DBNull.Value,
                    detalle.Precioproducto,
                    (object?)detalle.Descuento ?? DBNull.Value,
                    detalle.Valortproducto,
                    detalle.Valoriva,
                    detalle.Valortotal,
                    detalle.Tarifa,
                    (object?)detalle.Valorice ?? DBNull.Value,
                    (object?)detalle.Costo ?? DBNull.Value,
                    (object?)detalle.Bonificacion ?? DBNull.Value);
            }

            return table;
        }

        public async Task<List<FacturaListDto>> ListarFacturasAsync(int top = 200)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Facturas
                .AsNoTracking()
                .Include(f => f.CodclientesNavigation)
                .OrderByDescending(f => f.Codfactura)
                .Take(top)
                .Select(f => new FacturaListDto
                {
                    Codfactura = f.Codfactura,
                    Numfactura = f.Numfactura,
                    Total = f.Valortotal,
                    Tipopago = f.Tipopago,
                    Cliente = f.CodclientesNavigation != null
                        ? ((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? "")).Trim()
                        : null,
                    IdentificacionCliente = f.CodclientesNavigation != null
                        ? f.CodclientesNavigation.Numeroidentificacion
                        : null
                })
                .ToListAsync();
        }

        public async Task<List<PorcentajeIvaDto>> GetPorcentajesIvaActivosAsync()
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Porcentajeivas
                .AsNoTracking()
                .Where(x => x.Estado == "1" || x.Estado == "A")
                .Select(x => new PorcentajeIvaDto
                {
                    Codigo = x.Codigo,
                    Descripcion = x.Descripcion,
                    Valor = x.Valor,
                    ValorCalculo = x.ValorCalculo
                })
                .ToListAsync();
        }

        public async Task<List<FacturaListDto>> ListarFacturasUsuarioAsync(int idUsuario, int top = 200)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            IQueryable<Factura> query = context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario == idUsuario &&
                    (f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true) &&
                    (f.Notas == null || !f.Notas.Contains(MarcadorCompraDocumentosNotas)))
                .OrderByDescending(f => f.Codfactura);

            if (top > 0)
                query = query.Take(top);

            return await query
                .Select(f => new FacturaListDto
                {
                    Codfactura = f.Codfactura,
                    Numfactura = f.Numfactura,
                    Serie = f.Serie,
                    FechaEmision = f.Fechaentrega,
                    EstadoSri = f.Estadoenviosri,
                    Autorizado = f.Autorizado,
                    NumeroAutorizacion = f.Numautorizacion,
                    MensajeSri = f.Mensaje,
                    FechaAutorizacion = f.Fchautorizacion,
                    Total = f.Valortotal,
                    TotalAbonado = context.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0m,
                    Tipopago = f.Tipopago,
                    EstadoPago = f.Estadopago,
                    Estado = f.Estado,
                    Cliente = f.CodclientesNavigation != null
                        ? ((f.CodclientesNavigation.Nombrerazonsocial != null && f.CodclientesNavigation.Nombrerazonsocial != string.Empty)
                            ? f.CodclientesNavigation.Nombrerazonsocial
                            : (((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? "")).Trim()))
                        : null,
                    IdentificacionCliente = f.CodclientesNavigation != null
                        ? f.CodclientesNavigation.Numeroidentificacion
                        : null
                })
                .ToListAsync();
        }

        public async Task<(bool Success, string Message)> MarcarFacturaNoCobradaAsync(
            int codFactura,
            int idUsuario,
            CancellationToken cancellationToken = default)
        {
            if (codFactura <= 0 || idUsuario <= 0)
                return (false, "No se pudo identificar la factura.");

            await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var factura = await context.Facturas
                .Include(f => f.CodclientesNavigation)
                .FirstOrDefaultAsync(
                    f =>
                        f.Codfactura == codFactura &&
                        f.Idusuario == idUsuario &&
                        (f.CodemisorNavigation == null || !f.CodemisorNavigation.EsEmisorSistema) &&
                        (f.Notas == null || !f.Notas.Contains(MarcadorCompraDocumentosNotas)),
                    cancellationToken);

            if (factura is null)
                return (false, "No se encontró la factura.");

            if (factura.Estado == false)
                return (false, "Una factura anulada no puede enviarse a cuentas por cobrar.");

            if (!DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
                return (false, "La factura debe estar autorizada antes de enviarla a cuentas por cobrar.");

            if (!factura.Codclientes.HasValue)
                return (false, "La factura no tiene un cliente asociado.");

            var totalFactura = factura.Valortotal ?? 0m;
            if (totalFactura <= 0m)
                return (false, "La factura no tiene un valor pendiente válido.");

            var totalAbonado = await context.Abonos
                .Where(a => a.codFactura == codFactura && a.estado == true)
                .SumAsync(a => (decimal?)a.abono, cancellationToken) ?? 0m;
            var saldoPendiente = Math.Max(totalFactura - totalAbonado, 0m);

            if (saldoPendiente <= 0m)
                return (false, "La factura ya está completamente cobrada. Revisa primero sus abonos registrados.");

            factura.Estadopago = "PENDIENTE";
            factura.Fechacancelado = null;
            factura.Valorapagar = saldoPendiente;
            factura.Fechavence = DateTime.Today.AddDays(30);

            await context.SaveChangesAsync(cancellationToken);
            return (true, "La factura ahora está disponible en Cuentas por cobrar.");
        }

        public async Task<List<FacturaListDto>> ListarFacturasClienteUsuarioAsync(int idUsuario, string identificacionCliente, int top = 200)
        {
            if (string.IsNullOrWhiteSpace(identificacionCliente))
                return new List<FacturaListDto>();

            await using var context = await _dbFactory.CreateDbContextAsync();

            var identificacionNormalizada = identificacionCliente.Trim();

            IQueryable<Factura> query = context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario == idUsuario &&
                    (f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true) &&
                    (f.Notas == null || !f.Notas.Contains(MarcadorCompraDocumentosNotas)) &&
                    f.CodclientesNavigation != null &&
                    f.CodclientesNavigation.Numeroidentificacion == identificacionNormalizada)
                .OrderByDescending(f => f.Codfactura);

            if (top > 0)
                query = query.Take(top);

            return await query
                .Select(f => new FacturaListDto
                {
                    Codfactura = f.Codfactura,
                    Numfactura = f.Numfactura,
                    Serie = f.Serie,
                    FechaEmision = f.Fechaentrega,
                    EstadoSri = f.Estadoenviosri,
                    Autorizado = f.Autorizado,
                    NumeroAutorizacion = f.Numautorizacion,
                    MensajeSri = f.Mensaje,
                    FechaAutorizacion = f.Fchautorizacion,
                    Total = f.Valortotal,
                    Tipopago = f.Tipopago,
                    Cliente = f.CodclientesNavigation != null
                        ? ((f.CodclientesNavigation.Nombrerazonsocial != null && f.CodclientesNavigation.Nombrerazonsocial != string.Empty)
                            ? f.CodclientesNavigation.Nombrerazonsocial
                            : (((f.CodclientesNavigation.Nombres ?? "") + " " + (f.CodclientesNavigation.Apellidos ?? "")).Trim()))
                        : null,
                    IdentificacionCliente = f.CodclientesNavigation != null
                        ? f.CodclientesNavigation.Numeroidentificacion
                        : null
                })
                .ToListAsync();
        }

        public async Task<FacturaViewDto?> GetFacturaCompletaUsuarioAsync(
            int codfactura,
            int idUsuario,
            bool incluirFacturasEmisorSistema = false)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Codfactura == codfactura &&
                    f.Idusuario == idUsuario &&
                    (incluirFacturasEmisorSistema ||
                        ((f.CodemisorNavigation == null || f.CodemisorNavigation.EsEmisorSistema != true) &&
                         (f.Notas == null || !f.Notas.Contains(MarcadorCompraDocumentosNotas)))))
                .Select(f => new FacturaViewDto
                {
                    Factura = new Factura
                    {
                        Codfactura = f.Codfactura,
                        Codclientes = f.Codclientes,
                        Codemisor = f.Codemisor,
                        Coddocumento = f.Coddocumento,
                        Codtransportista = f.Codtransportista,
                        Numfactura = f.Numfactura,
                        Codclave = f.Codclave,
                        Numautorizacion = f.Numautorizacion,
                        Fchautorizacion = f.Fchautorizacion,
                        Fechaentrega = f.Fechaentrega,
                        Ambiente = f.Ambiente,
                        Subtotal12 = f.Subtotal12,
                        Subtotal0 = f.Subtotal0,
                        Subtotal = f.Subtotal,
                        Descuentos = f.Descuentos,
                        Iva = f.Iva,
                        Valortotal = f.Valortotal,
                        Tipopago = f.Tipopago,
                        Tiempocredito = f.Tiempocredito,
                        DescuentoGlobalPct = f.DescuentoGlobalPct,
                        DescuentoGlobalValor = f.DescuentoGlobalValor,
                        Serie = f.Serie,
                        Guiaremision = f.Guiaremision,
                        Notas = f.Notas,
                        Autorizado = f.Autorizado,
                        Estadoenviosri = f.Estadoenviosri,
                    },
                    Cliente = f.CodclientesNavigation == null ? null : new Cliente
                    {
                        Codcliente = f.CodclientesNavigation.Codcliente,
                        Nombres = f.CodclientesNavigation.Nombres,
                        Apellidos = f.CodclientesNavigation.Apellidos,
                        Nombrerazonsocial = f.CodclientesNavigation.Nombrerazonsocial,
                        Nombrecomercial = f.CodclientesNavigation.Nombrecomercial,
                        Numeroidentificacion = f.CodclientesNavigation.Numeroidentificacion,
                        Tipoidentificacion = f.CodclientesNavigation.Tipoidentificacion,
                        Direccion = f.CodclientesNavigation.Direccion,
                        Correo = f.CodclientesNavigation.Correo,
                        Telefonoconvencional = f.CodclientesNavigation.Telefonoconvencional,
                        Celular = f.CodclientesNavigation.Celular,
                        Referencia = f.CodclientesNavigation.Referencia,
                        Observaciones = f.CodclientesNavigation.Observaciones,
                        DiasCredito = f.CodclientesNavigation.DiasCredito
                    },
                    Emisor = f.CodemisorNavigation == null ? null : new Emisor
                    {
                        Codigo = f.CodemisorNavigation.Codigo,
                        Ruc = f.CodemisorNavigation.Ruc,
                        RazonSocial = f.CodemisorNavigation.RazonSocial,
                        NomComercial = f.CodemisorNavigation.NomComercial,
                        DirEstablecimiento = f.CodemisorNavigation.DirEstablecimiento,
                        CodEstablecimiento = f.CodemisorNavigation.CodEstablecimiento,
                        CodPuntoEmision = f.CodemisorNavigation.CodPuntoEmision,
                        DireccionMatriz = f.CodemisorNavigation.DireccionMatriz,
                        Telefono = f.CodemisorNavigation.Telefono,
                        LogoImagen = f.CodemisorNavigation.LogoImagen,
                        TipoEmision = f.CodemisorNavigation.TipoEmision,
                        TipoAmbiente = f.CodemisorNavigation.TipoAmbiente
                    },
                    FormaPagoNombre = context.FormasPago
                        .Where(fp => fp.Codigo == f.Tipopago)
                        .Select(fp => string.IsNullOrWhiteSpace(fp.DescripcionSri)
                            ? (fp.Descripcion ?? string.Empty)
                            : string.IsNullOrWhiteSpace(fp.Descripcion)
                                ? fp.DescripcionSri!
                                : fp.DescripcionSri == fp.Descripcion
                                    ? fp.DescripcionSri!
                                    : fp.DescripcionSri + " (" + fp.Descripcion + ")")
                        .FirstOrDefault() ?? string.Empty,
                    Detalles = f.Detallefacturas.ToList()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<string?> AsegurarXmlFacturaUsuarioAsync(int codfactura, int idUsuario, bool forzarRegeneracion = false)
        {
            var rutaXml = await AsegurarRutaXmlFacturaUsuarioAsync(codfactura, idUsuario, forzarRegeneracion);
            if (string.IsNullOrWhiteSpace(rutaXml))
                return null;

            return $"/FacturasGeneradas/{Path.GetFileName(rutaXml)}";
        }

        public async Task<string?> AsegurarRutaXmlFacturaUsuarioAsync(int codfactura, int idUsuario, bool forzarRegeneracion = false)
        {
            var facturaView = await GetFacturaCompletaUsuarioAsync(codfactura, idUsuario);
            if (facturaView?.Factura == null || string.IsNullOrWhiteSpace(facturaView.Emisor?.Ruc))
                return null;

            var rutaXml = ConstruirRutaLocalXmlFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura);

            if (forzarRegeneracion || !File.Exists(rutaXml))
            {
                try
                {
                    rutaXml = await ProcesarXmlFacturaAsync(codfactura);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "No se pudo regenerar el XML de la factura {FacturaId} por datos incompletos de autorizacion.", codfactura);
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
                return null;

            return ConstruirUrlXmlFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura);
        }

        public async Task<string?> AsegurarXmlFacturaAsync(int codfactura, bool forzarRegeneracion = false)
        {
            var facturaView = await GetFacturaCompletaAsync(codfactura);
            if (facturaView?.Factura == null || string.IsNullOrWhiteSpace(facturaView.Emisor?.Ruc))
                return null;

            if (EsFacturaBackOfficeSistema(facturaView))
                forzarRegeneracion = true;

            var rutaXml = ConstruirRutaLocalXmlFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura);

            if (forzarRegeneracion || !File.Exists(rutaXml))
            {
                try
                {
                    EliminarXmlFacturaGenerado(facturaView.Factura.Serie, facturaView.Factura.Numfactura);
                    rutaXml = await ProcesarXmlFacturaAsync(codfactura);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "No se pudo regenerar el XML de la factura {FacturaId} por datos incompletos de autorizacion.", codfactura);
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
                return null;

            return $"/FacturasGeneradas/{Path.GetFileName(rutaXml)}?v={File.GetLastWriteTimeUtc(rutaXml).Ticks}";
        }

        public async Task<string?> AsegurarPdfFacturaUsuarioAsync(int codfactura, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            var facturaView = await GetFacturaCompletaUsuarioAsync(codfactura, idUsuario);
            if (facturaView?.Factura == null || string.IsNullOrWhiteSpace(facturaView.Emisor?.Ruc))
                return null;

            facturaView.Factura.Codclave = await ResolverClaveAccesoFacturaAsync(facturaView);

            var rutaPdf = ConstruirRutaLocalPdfFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura,
                formato);

            var debeRegenerarPdf = !File.Exists(rutaPdf) || facturaView.Factura.Autorizado == true;

            if (debeRegenerarPdf)
            {
                try
                {
                    rutaPdf = await _facturaPdfService.GenerarPdfFacturaAsync(facturaView, formato);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "No se pudo regenerar el PDF de la factura {FacturaId} por datos incompletos de autorizacion.", codfactura);
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
                return null;

            return ConstruirUrlPdfFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura,
                formato);
        }

        public async Task<string?> AsegurarPdfFacturaAsync(int codfactura, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            var facturaView = await GetFacturaCompletaAsync(codfactura);
            if (facturaView?.Factura == null || string.IsNullOrWhiteSpace(facturaView.Emisor?.Ruc))
                return null;

            facturaView.Factura.Codclave = await ResolverClaveAccesoFacturaAsync(facturaView);

            var rutaPdf = ConstruirRutaLocalPdfFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura,
                formato);

            var debeRegenerarPdf = !File.Exists(rutaPdf) || facturaView.Factura.Autorizado == true;

            if (debeRegenerarPdf)
            {
                try
                {
                    rutaPdf = await _facturaPdfService.GenerarPdfFacturaAsync(facturaView, formato);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "No se pudo regenerar el PDF de la factura {FacturaId} por datos incompletos de autorizacion.", codfactura);
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
                return null;

            return ConstruirUrlPdfFactura(
                facturaView.Emisor.Ruc,
                facturaView.Factura.Serie,
                facturaView.Factura.Numfactura,
                formato);
        }

        private async Task<string?> ResolverClaveAccesoFacturaAsync(FacturaViewDto facturaView)
        {
            var claveActual = (facturaView.Factura.Codclave ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(claveActual))
                return claveActual;

            string? claveRecuperada = null;
            var ruc = (facturaView.Emisor?.Ruc ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(ruc))
            {
                var rutaXml = ConstruirRutaLocalXmlFactura(
                    ruc,
                    facturaView.Factura.Serie,
                    facturaView.Factura.Numfactura);

                if (File.Exists(rutaXml))
                    claveRecuperada = ExtraerClaveAccesoDesdeXml(rutaXml);
            }

            if (string.IsNullOrWhiteSpace(claveRecuperada) && !string.IsNullOrWhiteSpace(ruc))
            {
                var fechaEmision = facturaView.Factura.Fechaentrega ?? facturaView.Factura.Fchautorizacion ?? DateTime.Now;
                var ambiente = facturaView.Factura.Ambiente?.ToString() ?? facturaView.Emisor?.TipoAmbiente?.ToString() ?? "2";
                var serieLimpia = (facturaView.Factura.Serie ?? "001001").Replace("-", "");
                var secuencial = (facturaView.Factura.Numfactura ?? "1").PadLeft(9, '0');
                claveRecuperada = GenerarClaveAcceso(fechaEmision, ruc, ambiente, serieLimpia, secuencial, "1");
            }

            claveRecuperada = (claveRecuperada ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(claveRecuperada))
                return null;

            facturaView.Factura.Codclave = claveRecuperada;
            await GuardarClaveAccesoFacturaAsync(facturaView.Factura.Codfactura, claveRecuperada);
            return claveRecuperada;
        }

        private async Task GuardarClaveAccesoFacturaAsync(int codFactura, string claveAcceso)
        {
            if (string.IsNullOrWhiteSpace(claveAcceso))
                return;

            await using var context = await _dbFactory.CreateDbContextAsync();
            var facturaDb = await context.Facturas.FirstOrDefaultAsync(f => f.Codfactura == codFactura);
            if (facturaDb == null)
                return;

            var claveNormalizada = claveAcceso.Trim();
            if (string.Equals((facturaDb.Codclave ?? string.Empty).Trim(), claveNormalizada, StringComparison.Ordinal))
                return;

            facturaDb.Codclave = claveNormalizada;
            await context.SaveChangesAsync();
        }

        private static string? ExtraerClaveAccesoDesdeXml(string rutaXml)
        {
            try
            {
                var xml = XDocument.Load(rutaXml);
                return xml.Descendants()
                    .FirstOrDefault(x => string.Equals(x.Name.LocalName, "claveAcceso", StringComparison.OrdinalIgnoreCase))
                    ?.Value
                    ?.Trim();
            }
            catch
            {
                return null;
            }
        }

        public async Task<FacturaViewDto?> GetFacturaPorNumeroUsuarioAsync(string numFactura, int idUsuario, string? serie = null)
        {
            if (string.IsNullOrWhiteSpace(numFactura))
                return null;

            numFactura = numFactura.Trim();

            string? serieNorm = null;
            if (!string.IsNullOrWhiteSpace(serie))
                serieNorm = serie.Replace("-", "").Trim();

            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.Facturas
                .AsNoTracking()
                .Where(f =>
                    f.Idusuario == idUsuario &&
                    f.Numfactura == numFactura &&
                    (serieNorm == null || (f.Serie ?? "") == serieNorm))
                .Select(f => new FacturaViewDto
                {
                    Factura = new Factura
                    {
                        Codfactura = f.Codfactura,
                        Codclientes = f.Codclientes,
                        Codemisor = f.Codemisor,
                        Numfactura = f.Numfactura,
                        Numautorizacion = f.Numautorizacion,
                        Coddocumento = f.Coddocumento,
                        Codtransportista = f.Codtransportista,
                        Fchautorizacion = f.Fchautorizacion,
                        Fechaentrega = f.Fechaentrega,
                        Subtotal12 = f.Subtotal12,
                        Subtotal0 = f.Subtotal0,
                        Subtotal = f.Subtotal,
                        Descuentos = f.Descuentos,
                        Iva = f.Iva,
                        Valortotal = f.Valortotal,
                        Tipopago = f.Tipopago,
                        Tiempocredito = f.Tiempocredito,
                        DescuentoGlobalPct = f.DescuentoGlobalPct,
                        DescuentoGlobalValor = f.DescuentoGlobalValor,
                        Serie = f.Serie,
                        Guiaremision = f.Guiaremision,
                        Notas = f.Notas,
                        Codclave = f.Codclave,
                    },
                    Cliente = f.CodclientesNavigation == null ? null : new Cliente
                    {
                        Codcliente = f.CodclientesNavigation.Codcliente,
                        Nombres = f.CodclientesNavigation.Nombres,
                        Apellidos = f.CodclientesNavigation.Apellidos,
                        Nombrerazonsocial = f.CodclientesNavigation.Nombrerazonsocial,
                        Nombrecomercial = f.CodclientesNavigation.Nombrecomercial,
                        Numeroidentificacion = f.CodclientesNavigation.Numeroidentificacion,
                        TipoCliente = f.CodclientesNavigation.TipoCliente,
                        Tipoidentificacion = f.CodclientesNavigation.Tipoidentificacion,
                        Pais = f.CodclientesNavigation.Pais,
                        Provincia = f.CodclientesNavigation.Provincia,
                        Ciudad = f.CodclientesNavigation.Ciudad,
                        Telefonoconvencional = f.CodclientesNavigation.Telefonoconvencional,
                        Celular = f.CodclientesNavigation.Celular,
                        Correo = f.CodclientesNavigation.Correo,
                        Direccion = f.CodclientesNavigation.Direccion,
                        Observaciones = f.CodclientesNavigation.Observaciones,
                        Referencia = f.CodclientesNavigation.Referencia
                    },
                    Emisor = f.CodemisorNavigation == null ? null : new Emisor
                    {
                        Codigo = f.CodemisorNavigation.Codigo,
                        Ruc = f.CodemisorNavigation.Ruc,
                        RazonSocial = f.CodemisorNavigation.RazonSocial,
                        NomComercial = f.CodemisorNavigation.NomComercial,
                        DirEstablecimiento = f.CodemisorNavigation.DirEstablecimiento,
                        CodEstablecimiento = f.CodemisorNavigation.CodEstablecimiento,
                        CodPuntoEmision = f.CodemisorNavigation.CodPuntoEmision,
                        DireccionMatriz = f.CodemisorNavigation.DireccionMatriz,
                        Telefono = f.CodemisorNavigation.Telefono,
                        LogoImagen = f.CodemisorNavigation.LogoImagen
                    },
                    FormaPagoNombre = context.FormasPago
                        .Where(fp => fp.Codigo == f.Tipopago)
                        .Select(fp => string.IsNullOrWhiteSpace(fp.DescripcionSri)
                            ? (fp.Descripcion ?? string.Empty)
                            : string.IsNullOrWhiteSpace(fp.Descripcion)
                                ? fp.DescripcionSri!
                                : fp.DescripcionSri == fp.Descripcion
                                    ? fp.DescripcionSri!
                                    : fp.DescripcionSri + " (" + fp.Descripcion + ")")
                        .FirstOrDefault() ?? string.Empty,
                    Detalles = f.Detallefacturas.Select(d => new Detallefactura
                    {
                        Codlinea = d.Codlinea,
                        Codfactura = d.Codfactura,
                        Codproducto = d.Codproducto,
                        Codprincipal = d.Codprincipal,
                        Codauxiliar = d.Codauxiliar,
                        Cantproducto = d.Cantproducto,
                        Descripproducto = d.Descripproducto,
                        Precioproducto = d.Precioproducto,
                        Descuento = d.Descuento,
                        Valortproducto = d.Valortproducto,
                        Valoriva = d.Valoriva,
                        Valortotal = d.Valortotal,
                        Tarifa = d.Tarifa
                    }).ToList()
                })
                .FirstOrDefaultAsync();
        }

        public async Task<string> GetNextSecuencialNotaCreditoAsync(int idUsuario, string serieNc)
        {
            serieNc = (serieNc ?? "").Replace("-", "").Trim();
            if (idUsuario <= 0 || string.IsNullOrWhiteSpace(serieNc))
                return "000000001";

            await using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosCuenta = await ObtenerUsuariosCuentaIdsAsync(context, idUsuario);
            if (usuariosCuenta.Count == 0)
                usuariosCuenta.Add(idUsuario);

            var list = await context.NotaCreditos
                .Where(n => n.Usuario.HasValue &&
                            usuariosCuenta.Contains(n.Usuario.Value) &&
                            n.Serie != null &&
                            n.Serie.Replace("-", "") == serieNc)
                .Select(n => n.NumNotaCredito)
                .ToListAsync();

            int max = 0;
            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (int.TryParse(s.Trim(), out var n) && n > max)
                    max = n;
            }

            var estadoSecuencia = await _initialSequencePromptService.GetStateAsync(idUsuario, "nota-credito", serieNc);
            var automatico = max > 0 ? (max + 1).ToString("000000000") : string.Empty;
            var siguiente = _initialSequencePromptService.ResolveNextSequence(automatico, estadoSecuencia);
            return string.IsNullOrWhiteSpace(siguiente) ? "000000001" : siguiente;
        }

        public async Task<string> GetNextSecuencialNotaDebitoAsync(int idUsuario, string serieNd)
        {
            serieNd = (serieNd ?? "").Replace("-", "").Trim();
            if (idUsuario <= 0 || string.IsNullOrWhiteSpace(serieNd))
                return "000000001";

            await using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosCuenta = await ObtenerUsuariosCuentaIdsAsync(context, idUsuario);
            if (usuariosCuenta.Count == 0)
                usuariosCuenta.Add(idUsuario);

            var list = await context.NotaDebitos
                .Where(n => n.Usuario.HasValue &&
                            usuariosCuenta.Contains(n.Usuario.Value) &&
                            n.Serie != null &&
                            n.Serie.Replace("-", "") == serieNd)
                .Select(n => n.NumNotaDebito)
                .ToListAsync();

            var max = 0;
            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s))
                    continue;

                if (int.TryParse(s.Trim(), out var n) && n > max)
                    max = n;
            }

            var estadoSecuencia = await _initialSequencePromptService.GetStateAsync(idUsuario, "nota-debito", serieNd);
            var automatico = max > 0 ? (max + 1).ToString("000000000") : string.Empty;
            var siguiente = _initialSequencePromptService.ResolveNextSequence(automatico, estadoSecuencia);
            return string.IsNullOrWhiteSpace(siguiente) ? "000000001" : siguiente;
        }

        public async Task<FacturaViewDto?> GetFacturaCompletaAsync(int codfactura)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var facturaView = await context.Facturas
                .AsNoTracking()
                .Where(f => f.Codfactura == codfactura)
                .Select(f => new FacturaViewDto
                {
                    Factura = new Factura
                    {
                        Codfactura = f.Codfactura,
                        Codclientes = f.Codclientes,
                        Codemisor = f.Codemisor,
                        Coddocumento = f.Coddocumento,
                        Codtransportista = f.Codtransportista,
                        Numfactura = f.Numfactura,
                        Numautorizacion = f.Numautorizacion,
                        Fchautorizacion = f.Fchautorizacion,
                        Fechaentrega = f.Fechaentrega,
                        Ambiente = f.Ambiente,
                        Subtotal12 = f.Subtotal12,
                        Subtotal0 = f.Subtotal0,
                        Subtotal = f.Subtotal,
                        Descuentos = f.Descuentos,
                        Iva = f.Iva,
                        Valortotal = f.Valortotal,
                        Tipopago = f.Tipopago,
                        Tiempocredito = f.Tiempocredito,
                        DescuentoGlobalPct = f.DescuentoGlobalPct,
                        DescuentoGlobalValor = f.DescuentoGlobalValor,
                        Serie = f.Serie,
                        Guiaremision = f.Guiaremision,
                        Notas = f.Notas,
                        Codclave = f.Codclave,
                        Autorizado = f.Autorizado,
                        Estadoenviosri = f.Estadoenviosri,
                    },
                    Cliente = f.CodclientesNavigation == null ? null : new Cliente
                    {
                        Codcliente = f.CodclientesNavigation.Codcliente,
                        Nombres = f.CodclientesNavigation.Nombres,
                        Apellidos = f.CodclientesNavigation.Apellidos,
                        Nombrerazonsocial = f.CodclientesNavigation.Nombrerazonsocial,
                        Nombrecomercial = f.CodclientesNavigation.Nombrecomercial,
                        Numeroidentificacion = f.CodclientesNavigation.Numeroidentificacion,
                        Tipoidentificacion = f.CodclientesNavigation.Tipoidentificacion,
                        Direccion = f.CodclientesNavigation.Direccion,
                        Correo = f.CodclientesNavigation.Correo,
                        Telefonoconvencional = f.CodclientesNavigation.Telefonoconvencional,
                        Celular = f.CodclientesNavigation.Celular,
                        Referencia = f.CodclientesNavigation.Referencia,
                        Observaciones = f.CodclientesNavigation.Observaciones,
                        DiasCredito = f.CodclientesNavigation.DiasCredito
                    },
                    Emisor = f.CodemisorNavigation == null ? null : new Emisor
                    {
                        Codigo = f.CodemisorNavigation.Codigo,
                        Ruc = f.CodemisorNavigation.Ruc,
                        RazonSocial = f.CodemisorNavigation.RazonSocial,
                        NomComercial = f.CodemisorNavigation.NomComercial,
                        DirEstablecimiento = f.CodemisorNavigation.DirEstablecimiento,
                        CodEstablecimiento = f.CodemisorNavigation.CodEstablecimiento,
                        CodPuntoEmision = f.CodemisorNavigation.CodPuntoEmision,
                        DireccionMatriz = f.CodemisorNavigation.DireccionMatriz,
                        Telefono = f.CodemisorNavigation.Telefono,
                        LogoImagen = f.CodemisorNavigation.LogoImagen,
                        TipoEmision = f.CodemisorNavigation.TipoEmision,
                        TipoAmbiente = f.CodemisorNavigation.TipoAmbiente
                    },
                    FormaPagoNombre = context.FormasPago
                        .Where(fp => fp.Codigo == f.Tipopago)
                        .Select(fp => string.IsNullOrWhiteSpace(fp.DescripcionSri)
                            ? (fp.Descripcion ?? string.Empty)
                            : string.IsNullOrWhiteSpace(fp.Descripcion)
                                ? fp.DescripcionSri!
                                : fp.DescripcionSri == fp.Descripcion
                                    ? fp.DescripcionSri!
                                    : fp.DescripcionSri + " (" + fp.Descripcion + ")")
                        .FirstOrDefault() ?? string.Empty,
                    Detalles = f.Detallefacturas.Select(d => new Detallefactura
                    {
                        Codlinea = d.Codlinea,
                        Codfactura = d.Codfactura,
                        Codproducto = d.Codproducto,
                        Codprincipal = d.Codprincipal,
                        Codauxiliar = d.Codauxiliar,
                        Cantproducto = d.Cantproducto,
                        Descripproducto = d.Descripproducto,
                        Precioproducto = d.Precioproducto,
                        Descuento = d.Descuento,
                        Valortproducto = d.Valortproducto,
                        Valoriva = d.Valoriva,
                        Valortotal = d.Valortotal,
                        Tarifa = d.Tarifa,
                        Valorice = d.Valorice,
                        Costo = d.Costo,
                        Bonificacion = d.Bonificacion
                    }).ToList()
                })
                .SingleOrDefaultAsync();

            await AplicarEmisorSistemaAFacturaViewAsync(context, facturaView);
            return facturaView;
        }

        public string GenerarClaveAcceso(DateTime fecha, string ruc, string ambiente, string serie, string secuencial, string tipoEmi)
        {
            string fechaStr = fecha.ToString("ddMMyyyy");
            string tipoComp = "01";
            string rucLimpio = ruc?.Trim().PadLeft(13, '0') ?? "";
            string serieLimpia = serie?.Replace("-", "").Trim().PadLeft(6, '0') ?? "001001";
            string secuencialLimpio = secuencial?.Trim().PadLeft(9, '0') ?? "000000001";
            string codNumerico = "12345678";

            string clave48 = fechaStr + tipoComp + rucLimpio + ambiente + serieLimpia + secuencialLimpio + codNumerico + tipoEmi;

            int suma = 0;
            int factor = 2;
            for (int i = clave48.Length - 1; i >= 0; i--)
            {
                suma += (int)char.GetNumericValue(clave48[i]) * factor;
                factor = factor == 7 ? 2 : factor + 1;
            }

            int verificador = 11 - (suma % 11);
            if (verificador == 11) verificador = 0;
            if (verificador == 10) verificador = 1;

            return clave48 + verificador.ToString();
        }

        private int CalcularModulo11(string clave)
        {
            int[] factores = { 2, 3, 4, 5, 6, 7 };
            int suma = 0;
            int factorIndex = 0;

            for (int i = clave.Length - 1; i >= 0; i--)
            {
                suma += (int)char.GetNumericValue(clave[i]) * factores[factorIndex];
                factorIndex = (factorIndex == 5) ? 0 : factorIndex + 1;
            }

            int residuo = suma % 11;
            int resultado = 11 - residuo;

            if (resultado == 11) return 0;
            if (resultado == 10) return 1;
            return resultado;
        }

        public async Task<List<FormasPago>> ObtenerFormasPagoAsync()
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            return await context.FormasPago
                .AsNoTracking()
                .Where(f => f.Estado == true && f.TipoVenta == true)
                .ToListAsync();
        }

        public string CalcularTarifaPredominante(List<Detallefactura> detalles)
        {
            var tarifaMasFrecuente = detalles
                .GroupBy(d => d.Tarifa)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            return tarifaMasFrecuente switch
            {
                0 => "0",
                12 => "2",
                15 => "4",
                _ => "4"
            };
        }

        private static string ObtenerCodigoPorcentajeFacturaSri(int tarifa)
            => tarifa <= 0 ? "0" : "4";

        private static string ResolverAmbienteSriFactura(Factura factura)
        {
            if (factura.CodemisorNavigation?.EsEmisorSistema == true)
                return "2";

            var ambienteEmisor = factura.CodemisorNavigation?.TipoAmbiente?.Trim();
            if (ambienteEmisor is "1" or "2")
                return ambienteEmisor;

            if (factura.Ambiente is 1 or 2)
                return factura.Ambiente.Value.ToString(CultureInfo.InvariantCulture);

            return "2";
        }

        public string GenerarXmlFactura(Factura factura, List<Detallefactura> detalles, string codigoFormaPago)
        {
            var cultura = CultureInfo.InvariantCulture;
            string ambiente = ResolverAmbienteSriFactura(factura);
            string serieLimpia = factura.Serie?.Replace("-", "") ?? "001001";
            string secuencial = factura.Numfactura?.PadLeft(9, '0') ?? "000000001";
            var fechaEmision = (factura.Fechaentrega ?? factura.Fchautorizacion ?? DateTime.Now).Date;
            var identificacionComprador = (factura.CodclientesNavigation?.Numeroidentificacion ?? string.Empty).Trim();
            var tipoIdentificacionComprador = ResolverTipoIdentificacionCompradorXml(
                factura.CodclientesNavigation?.Tipoidentificacion,
                identificacionComprador);
            string? guiaRemisionXml = FormatearNumeroGuiaRemision(factura.Guiaremision);
            decimal baseDescuentoGlobal = detalles.Sum(d => Math.Round(d.Valortproducto, 2));
            decimal descuentoGlobalTotal = factura.DescuentoGlobalValor
                ?? Math.Round(baseDescuentoGlobal * (factura.DescuentoGlobalPct ?? 0m), 2);
            descuentoGlobalTotal = Math.Max(0m, Math.Min(Math.Round(descuentoGlobalTotal, 2), baseDescuentoGlobal));
            bool usarDescuentoGlobalXml = descuentoGlobalTotal > 0m;
            decimal descuentoGlobalPendiente = descuentoGlobalTotal;
            decimal totalDescuentoLinea = detalles.Sum(d => Math.Round(d.Descuento ?? 0m, 2));
            decimal totalDescuentoXml = Math.Round(totalDescuentoLinea + (usarDescuentoGlobalXml ? descuentoGlobalTotal : 0m), 2);
            totalDescuentoXml = Math.Max(0m, Math.Min(totalDescuentoXml, baseDescuentoGlobal));

            ValidarDatosAutorizacionFactura(factura);

            string claveEsperada = GenerarClaveAcceso(
                fechaEmision,
                factura.CodemisorNavigation?.Ruc,
                ambiente,
                serieLimpia,
                secuencial,
                "1");
            string claveAcceso = string.Equals(
                (factura.Codclave ?? string.Empty).Trim(),
                claveEsperada,
                StringComparison.Ordinal)
                    ? factura.Codclave!.Trim()
                    : claveEsperada;

            factura.Codclave = claveAcceso;
            factura.Ambiente = int.TryParse(ambiente, out var ambienteNumerico) ? ambienteNumerico : 2;

            var infoAdicional = new List<XElement>();
            AgregarCampoAdicional(infoAdicional, "EmailCliente", factura.CodclientesNavigation?.Correo);
            AgregarCampoAdicional(infoAdicional, "TelefonoCliente", factura.CodclientesNavigation?.Celular);
            AgregarCampoAdicional(infoAdicional, "EmailEmisor", factura.CodemisorNavigation?.Email);
            AgregarCampoAdicional(infoAdicional, "TelefonoEmisor", factura.CodemisorNavigation?.Telefono);
            AgregarCampoAdicional(infoAdicional, "DatosBancarios", factura.Notas);

            if (DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
            {
                AgregarCampoAdicional(infoAdicional, "ClaveAcceso", claveAcceso);
                AgregarCampoAdicional(infoAdicional, "NumeroAutorizacion", factura.Numautorizacion);
                AgregarCampoAdicional(infoAdicional, "FechaAutorizacion", (factura.Fchautorizacion ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm:ss"));
            }

            XElement xml = new XElement("factura",
                new XAttribute("id", "comprobante"),
                new XAttribute("version", "1.1.0"),
                new XElement("infoTributaria",
                    new XElement("ambiente", ambiente),
                    new XElement("tipoEmision", "1"),
                    new XElement("razonSocial", factura.CodemisorNavigation?.RazonSocial),
                    new XElement("nombreComercial", factura.CodemisorNavigation?.NomComercial),
                    new XElement("ruc", factura.CodemisorNavigation?.Ruc),
                    new XElement("claveAcceso", claveAcceso),
                    new XElement("codDoc", "01"),
                    new XElement("estab", serieLimpia.Substring(0, 3)),
                    new XElement("ptoEmi", serieLimpia.Substring(3, 3)),
                    new XElement("secuencial", secuencial),
                    new XElement("dirMatriz", factura.CodemisorNavigation?.DireccionMatriz),
                    factura.CodemisorNavigation?.Retenciones == "SI"
                    ? new XElement("agenteRetencion", 1)
                    : null
                ),
                new XElement("infoFactura",
                    new XElement("fechaEmision", fechaEmision.ToString("dd/MM/yyyy")),
                    new XElement("dirEstablecimiento", factura.CodemisorNavigation?.DirEstablecimiento ?? factura.CodemisorNavigation?.DireccionMatriz),
                    new XElement("obligadoContabilidad", factura.CodemisorNavigation?.LlevaContabilidad),
                    new XElement("tipoIdentificacionComprador", tipoIdentificacionComprador),
                    new XElement("razonSocialComprador",
                        !string.IsNullOrWhiteSpace(factura.CodclientesNavigation?.Nombrerazonsocial)
                            ? factura.CodclientesNavigation.Nombrerazonsocial
                            : ((factura.CodclientesNavigation?.Nombres ?? "") + " " + (factura.CodclientesNavigation?.Apellidos ?? "")).Trim()),
                    new XElement("identificacionComprador", identificacionComprador),
                    new XElement("direccionComprador", factura.CodclientesNavigation?.Direccion ?? "SANTO DOMINGO"),
                    !string.IsNullOrWhiteSpace(guiaRemisionXml)
                        ? new XElement("guiaRemision", guiaRemisionXml)
                        : null,
                    new XElement("totalSinImpuestos", (factura.Subtotal ?? 0).ToString("F2", cultura)),
                    new XElement("totalDescuento", totalDescuentoXml.ToString("F2", cultura)),
                    new XElement("totalConImpuestos",
                        new XElement("totalImpuesto",
                            new XElement("codigo", "2"),
                            new XElement("codigoPorcentaje", ObtenerCodigoPorcentajeFacturaSri(detalles.FirstOrDefault(d => d.Tarifa > 0)?.Tarifa ?? 0)),
                            new XElement("baseImponible", (factura.Subtotal ?? 0).ToString("F2", cultura)),
                            new XElement("valor", (factura.Iva ?? 0).ToString("F2", cultura))
                        )
                    ),
                     new XElement("propina", "0.00"),
                    new XElement("importeTotal", (factura.Valortotal ?? 0).ToString("F2", cultura)),
                    new XElement("moneda", "DOLAR"),                 
                    new XElement("pagos",
                        new XElement("pago",
                            new XElement("formaPago", codigoFormaPago),
                            new XElement("total", (factura.Valortotal ?? 0).ToString("F2", cultura)),
                            factura.Tiempocredito.HasValue && factura.Tiempocredito.Value > 0
                                ? new XElement("plazo", factura.Tiempocredito.Value.ToString(cultura))
                                : null,
                            factura.Tiempocredito.HasValue && factura.Tiempocredito.Value > 0
                                ? new XElement("unidadTiempo", "dias")
                                : null
                        )
                    )
                ),
                new XElement("detalles",
                    detalles.Select((d, index) =>
                    {
                        var descuentoLinea = Math.Round(d.Descuento ?? 0m, 2);
                        var baseLinea = Math.Round(d.Valortproducto, 2);
                        var descuentoXml = descuentoLinea;

                        if (usarDescuentoGlobalXml && descuentoXml <= 0m)
                        {
                            var descuentoGlobalLinea = index == detalles.Count - 1
                                ? Math.Round(descuentoGlobalPendiente, 2)
                                : Math.Round(
                                    baseDescuentoGlobal <= 0m ? 0m : descuentoGlobalTotal * (baseLinea / baseDescuentoGlobal),
                                    2,
                                    MidpointRounding.AwayFromZero);

                            descuentoGlobalLinea = Math.Max(0m, Math.Min(descuentoGlobalLinea, baseLinea));
                            descuentoGlobalPendiente = Math.Max(0m, Math.Round(descuentoGlobalPendiente - descuentoGlobalLinea, 2));
                            descuentoXml = descuentoGlobalLinea;
                        }

                        var baseImponibleXml = Math.Max(0m, Math.Round(baseLinea - descuentoXml, 2));
                        var codigoPrincipal = ResolverCodigoDetalleXml(d, index, factura);
                        var codigoAuxiliar = string.IsNullOrWhiteSpace(d.Codauxiliar)
                            ? codigoPrincipal
                            : d.Codauxiliar.Trim();
                        var descripcionDetalle = NormalizarTextoUnaLineaXml(
                            d.Descripproducto,
                            "Recarga de documentos");

                        return new XElement("detalle",
                            new XElement("codigoPrincipal", codigoPrincipal),
                            new XElement("codigoAuxiliar", codigoAuxiliar),
                            new XElement("descripcion", descripcionDetalle),
                            new XElement("cantidad", d.Cantproducto.ToString("F2", cultura)),
                            new XElement("precioUnitario", d.Precioproducto.ToString("F2", cultura)),
                            new XElement("descuento", descuentoXml.ToString("F2", cultura)),
                            new XElement("precioTotalSinImpuesto", baseImponibleXml.ToString("F2", cultura)),
                            new XElement("impuestos",
                                new XElement("impuesto",
                                    new XElement("codigo", "2"),
                                    new XElement("codigoPorcentaje", ObtenerCodigoPorcentajeFacturaSri(d.Tarifa)),
                                    new XElement("tarifa", d.Tarifa.ToString("F0", cultura)),
                                    new XElement("baseImponible", baseImponibleXml.ToString("F2", cultura)),
                                    new XElement("valor", d.Valoriva.ToString("F2", cultura))
                                )
                            )
                        );
                    })
                ),
                infoAdicional.Count > 0
                    ? new XElement("infoAdicional", infoAdicional)
                    : null
            );

            var document = new XDocument(new XDeclaration("1.0", "utf-8", null), xml);
            return document.ToString();
        }

        private static void AgregarCampoAdicional(ICollection<XElement> campos, string nombre, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
                campos.Add(new XElement("campoAdicional", new XAttribute("nombre", nombre), valor.Trim()));
        }

        private static string NormalizarTextoUnaLineaXml(string? valor, string reemplazo)
        {
            var texto = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor;
            return string.Join(
                " ",
                texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string ResolverTipoIdentificacionCompradorXml(string? tipoIdentificacionActual, string? identificacionComprador)
        {
            var digitos = new string((identificacionComprador ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digitos.Length == 10)
                return "05";

            if (digitos.Length == 13)
                return "04";

            return string.IsNullOrWhiteSpace(tipoIdentificacionActual) ? "05" : tipoIdentificacionActual.Trim();
        }

        private static string ResolverCodigoDetalleXml(Detallefactura detalle, int index, Factura factura)
        {
            if (!string.IsNullOrWhiteSpace(detalle.Codprincipal))
                return detalle.Codprincipal.Trim();

            if (detalle.Codproducto > 0)
                return detalle.Codproducto.ToString(CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(factura.Notas) &&
                factura.Notas.Contains(MarcadorCompraDocumentosNotas, StringComparison.OrdinalIgnoreCase))
            {
                return "001";
            }

            return "001";
        }

        public async Task<string> ProcesarXmlFacturaAsync(int idFactura)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var factura = await context.Facturas
                .AsSplitQuery()
                .Include(f => f.CodemisorNavigation)
                .Include(f => f.CodclientesNavigation)
                .Include(f => f.Detallefacturas)
                .FirstOrDefaultAsync(f => f.Codfactura == idFactura);

            if (factura == null)
                return "Error: Factura no encontrada";

            var emisor = await ResolverEmisorFacturaAsync(context, factura);
            if (emisor == null)
                throw new InvalidOperationException("No se encontro el emisor de la factura para generar el XML.");

            factura.CodemisorNavigation = emisor;
            factura.Codemisor = emisor.Codigo;
            if (factura.CodclientesNavigation != null)
            {
                var identificacionComprador = new string((factura.CodclientesNavigation.Numeroidentificacion ?? string.Empty)
                    .Where(char.IsDigit)
                    .ToArray());

                if (identificacionComprador.Length == 10)
                    factura.CodclientesNavigation.Tipoidentificacion = "05";
                else if (identificacionComprador.Length == 13)
                    factura.CodclientesNavigation.Tipoidentificacion = "04";
            }

            string formaPago = !string.IsNullOrWhiteSpace(factura.Tipopago) ? factura.Tipopago : "01";
            string xmlContenido = GenerarXmlFactura(factura, factura.Detallefacturas.ToList(), formaPago);

            if (context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync();

            string carpetaPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "FacturasGeneradas");
            if (!Directory.Exists(carpetaPath))
                Directory.CreateDirectory(carpetaPath);

            EliminarXmlFacturaGenerado(factura.Serie, factura.Numfactura);
            string nombreArchivo = $"{factura.CodemisorNavigation?.Ruc}_{factura.Serie}_{factura.Numfactura}.xml";
            string rutaCompleta = Path.Combine(carpetaPath, nombreArchivo);

            await File.WriteAllTextAsync(rutaCompleta, xmlContenido, System.Text.Encoding.UTF8);

            return rutaCompleta;
        }

        private static void EliminarXmlFacturaGenerado(string? serie, string? numero)
        {
            if (string.IsNullOrWhiteSpace(serie) || string.IsNullOrWhiteSpace(numero))
                return;

            var carpetaPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "FacturasGeneradas");
            if (!Directory.Exists(carpetaPath))
                return;

            var patron = $"*_{serie}_{numero}.xml";
            foreach (var ruta in Directory.GetFiles(carpetaPath, patron, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(ruta);
                }
                catch
                {
                }
            }
        }

        private static async Task GuardarXmlAutorizadoFacturaAsync(Factura factura, string xmlAutorizado)
        {
            if (factura.CodemisorNavigation == null ||
                string.IsNullOrWhiteSpace(factura.CodemisorNavigation.Ruc) ||
                string.IsNullOrWhiteSpace(factura.Serie) ||
                string.IsNullOrWhiteSpace(factura.Numfactura) ||
                string.IsNullOrWhiteSpace(xmlAutorizado))
            {
                return;
            }

            var carpetaPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "FacturasGeneradas");
            if (!Directory.Exists(carpetaPath))
                Directory.CreateDirectory(carpetaPath);

            var rutaCompleta = Path.Combine(
                carpetaPath,
                ConstruirNombreArchivoFactura(
                    factura.CodemisorNavigation.Ruc,
                    factura.Serie,
                    factura.Numfactura,
                    "xml"));

            await File.WriteAllTextAsync(rutaCompleta, xmlAutorizado, Encoding.UTF8);
        }

        private static string ConstruirUrlXmlFactura(string? ruc, string? serie, string? numero)
            => $"/FacturasGeneradas/{ConstruirNombreArchivoFactura(ruc, serie, numero, "xml")}";

        private static string ConstruirUrlPdfFactura(string? ruc, string? serie, string? numero, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
            => $"/FacturasGeneradas/{ConstruirNombreArchivoFactura(ruc, serie, numero, "pdf", formato)}";

        private static string ConstruirRutaLocalXmlFactura(string? ruc, string? serie, string? numero)
            => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "FacturasGeneradas", ConstruirNombreArchivoFactura(ruc, serie, numero, "xml"));

        private static string ConstruirRutaLocalPdfFactura(string? ruc, string? serie, string? numero, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
            => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "FacturasGeneradas", ConstruirNombreArchivoFactura(ruc, serie, numero, "pdf", formato));

        private static string ConstruirNombreArchivoFactura(string? ruc, string? serie, string? numero, string extension, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            var rucSeguro = LimpiarSegmentoArchivoFactura(ruc, "factura");
            var serieSegura = LimpiarSegmentoArchivoFactura(serie, "001001");
            var numeroSeguro = LimpiarSegmentoArchivoFactura(numero, "000000001");
            return $"{rucSeguro}_{serieSegura}_{numeroSeguro}{formato.ObtenerSufijoArchivo()}.{extension}";
        }

        private static string LimpiarSegmentoArchivoFactura(string? valor, string reemplazo)
        {
            var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();

            foreach (var caracter in Path.GetInvalidFileNameChars())
                limpio = limpio.Replace(caracter, '_');

            return limpio.Replace(" ", "_");
        }

        private static void ValidarDatosAutorizacionFactura(Factura factura)
        {
            if (!DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
                return;

            if (string.IsNullOrWhiteSpace(factura.Codclave) ||
                string.IsNullOrWhiteSpace(factura.Numautorizacion) ||
                !factura.Fchautorizacion.HasValue)
            {
                throw new InvalidOperationException(
                    "La factura esta autorizada pero no tiene completos los datos de autorizacion (clave de acceso, numero o fecha).");
            }
        }

        public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarFacturaPorCorreoAsync(int idFactura, string? rutaXmlExistente = null, mensajeSRI? m = null, bool forzarReenvio = false)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            //string rutaDestino = @"C:\Users\numerica\Desktop\efact2026\Simetric\wwwroot\FacturasGeneradas\1710892413001_001002_000000007.xml";

            if (!string.IsNullOrWhiteSpace(rutaXmlExistente) &&
                !string.IsNullOrWhiteSpace(m?.xml) &&
                string.Equals(m.estado, "AUTORIZADO", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string? directorio = Path.GetDirectoryName(rutaXmlExistente);
                    if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
                        Directory.CreateDirectory(directorio);

                    File.WriteAllText(rutaXmlExistente, m.xml, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error al grabar el XML autorizado en la ruta especificada: {ex.Message}");
                }
            }


            var factura = await context.Facturas
                .Include(f => f.CodemisorNavigation)
                .Include(f => f.CodclientesNavigation)
                .FirstOrDefaultAsync(f => f.Codfactura == idFactura);

            if (factura == null)
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    Mensaje = "No se encontró la factura para el envío por correo."
                };
            }

            var seguimiento = await _comprobanteCorreoEstadoService.GetEstadoAsync(
                ComprobanteCorreoEstadoService.TipoFactura,
                idFactura);

            var metadata = LeerFacturaCorreoMetadata(factura.Detalleextra);
            var destinatariosBase = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
                context,
                factura.Idusuario,
                factura.Codclientes,
                factura.CodclientesNavigation?.Correo);

            var destinatarios = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(
                metadata.Destinatarios
                    .Concat(ParseCorreosFactura(factura.Correoad))
                    .Concat(destinatariosBase));

            var correoUsuario = await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == factura.Idusuario)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(correoUsuario))
            {
                destinatarios = destinatarios
                    .Where(correo => !string.Equals(correo, correoUsuario.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            Debug.WriteLine($"[FACTURA-CORREO] Factura: {idFactura} | ForzarReenvio: {forzarReenvio}");
            Debug.WriteLine($"[FACTURA-CORREO] Cliente correo principal: {factura.CodclientesNavigation?.Correo}");
            Debug.WriteLine($"[FACTURA-CORREO] Metadata destinatarios: {string.Join(", ", metadata.Destinatarios)}");
            Debug.WriteLine($"[FACTURA-CORREO] Correoad: {factura.Correoad}");
            Debug.WriteLine($"[FACTURA-CORREO] Destinatarios finales: {string.Join(", ", destinatarios)}");
            _logger.LogInformation(
                "Factura {FacturaId} correo - reenvio:{ForzarReenvio} - cliente:{CorreoCliente} - destinatarios:{Destinatarios}",
                idFactura,
                forzarReenvio,
                factura.CodclientesNavigation?.Correo ?? "(sin correo principal)",
                string.Join(", ", destinatarios));

            if (!forzarReenvio && (seguimiento?.CorreoEnviado == true || metadata.CorreoEnviado))
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    YaEnviado = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = destinatarios.Any()
                        ? $"El correo de esta factura ya fue enviado anteriormente a {destinatarios.Count} destinatario(s)."
                        : "El correo de esta factura ya fue enviado anteriormente."
                };
            }

            if (!destinatarios.Any())
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    SinDestinatarios = true,
                    Mensaje = "La factura no tiene correos configurados para el cliente."
                };
            }

            if (factura.Autorizado != true)
            {
                await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                    ComprobanteCorreoEstadoService.TipoFactura,
                    idFactura);

                return new FacturaCorreoEnvioResultadoDto
                {
                    PendienteAutorizacion = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"La factura aun no esta autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s)."
                };
            }

            var rutaXml = rutaXmlExistente;
            if (forzarReenvio)
            {
                var rutaXmlActual = ConstruirRutaLocalXmlFactura(
                    factura.CodemisorNavigation?.Ruc,
                    factura.Serie,
                    factura.Numfactura);

                try
                {
                    if (!string.IsNullOrWhiteSpace(rutaXmlActual) && File.Exists(rutaXmlActual))
                    {
                        File.Delete(rutaXmlActual);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo eliminar el XML previo de la factura {FacturaId} antes del reenvio.", idFactura);
                }

                rutaXml = await ProcesarXmlFacturaAsync(idFactura);
            }

            if (string.IsNullOrWhiteSpace(rutaXml))
            {
                rutaXml = ConstruirRutaLocalXmlFactura(
                    factura.CodemisorNavigation?.Ruc,
                    factura.Serie,
                    factura.Numfactura);
            }

            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
                rutaXml = await ProcesarXmlFacturaAsync(idFactura);

            Debug.WriteLine($"[FACTURA-CORREO] Ruta XML: {rutaXml}");

            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            {
                metadata.Destinatarios = destinatarios;
                metadata.UltimoErrorCorreo = "No se pudo generar o ubicar el XML adjunto para el correo.";
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadata);
                await context.SaveChangesAsync();

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = "No se pudo generar o ubicar el XML adjunto para enviar la factura por correo."
                };
            }

            try
            {
                var facturaView = await GetFacturaCompletaAsync(idFactura)
                    ?? throw new InvalidOperationException("No se pudo cargar el detalle de la factura para generar el PDF adjunto.");

                var rutaPdf = await _facturaPdfService.GenerarPdfFacturaAsync(facturaView);
                if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
                    throw new FileNotFoundException("No se pudo generar o ubicar el PDF adjunto para el correo.", rutaPdf);

                Debug.WriteLine($"[FACTURA-CORREO] Ruta PDF: {rutaPdf}");
                _logger.LogInformation(
                    "Factura {FacturaId} correo - adjuntos XML:{RutaXml} PDF:{RutaPdf}",
                    idFactura,
                    rutaXml,
                    rutaPdf);

                await _emailService.EnviarFacturaAsync(
                    ObtenerNumeroFacturaDocumento(factura),
                    destinatarios,
                    ObtenerNombreClienteFactura(factura),
                    factura.Valortotal,
                    rutaXml,
                    rutaPdf);

                metadata.Destinatarios = destinatarios;
                metadata.CorreoEnviado = true;
                metadata.FechaEnvioCorreo = DateTime.Now;
                metadata.UltimoErrorCorreo = null;
                factura.Correoad = SerializarCorreosFactura(destinatarios);
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadata);
                await context.SaveChangesAsync();
                await _comprobanteCorreoEstadoService.MarcarEnviadoAsync(
                    ComprobanteCorreoEstadoService.TipoFactura,
                    idFactura);

                try
                {
                    await _auditService.RegistrarAuditoriaAsync(
                        factura.Idusuario,
                        "FACTURA_CORREO_ENVIADO",
                        null,
                        new
                        {
                            factura.Codfactura,
                            factura.Numfactura,
                            Destinatarios = destinatarios.Count
                        },
                        new
                        {
                            Correos = destinatarios,
                            FechaEnvio = metadata.FechaEnvioCorreo
                        });
                }
                catch { }

                return new FacturaCorreoEnvioResultadoDto
                {
                    Enviado = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = forzarReenvio
                        ? $"Factura reenviada correctamente por correo a {destinatarios.Count} destinatario(s)."
                        : $"Factura enviada correctamente por correo a {destinatarios.Count} destinatario(s)."
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FACTURA-CORREO][ERROR] Factura {idFactura}: {ex}");
                _logger.LogError(ex, "Error en envio de correo de factura {FacturaId}.", idFactura);
                metadata.Destinatarios = destinatarios;
                metadata.CorreoEnviado = false;
                metadata.UltimoErrorCorreo = ex.Message;
                factura.Correoad = SerializarCorreosFactura(destinatarios);
                factura.Detalleextra = EscribirFacturaCorreoMetadata(metadata);
                await context.SaveChangesAsync();
                await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                    ComprobanteCorreoEstadoService.TipoFactura,
                    idFactura,
                    ex.Message);

                try
                {
                    await _auditService.RegistrarAuditoriaAsync(
                        factura.Idusuario,
                        "ERROR_ENVIO_CORREO_FACTURA",
                        null,
                        new
                        {
                            factura.Codfactura,
                            factura.Numfactura,
                            Destinatarios = destinatarios.Count
                        },
                        new
                        {
                            Correos = destinatarios,
                            Error = ex.Message
                        });
                }
                catch { }

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"La factura no pudo enviarse por correo: {ex.Message}"
                };
            }
        }

        public async Task<List<int>> GetFacturasAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
        {
            var idsPendientes = await _comprobanteCorreoEstadoService.GetDocumentosPendientesAsync(
                ComprobanteCorreoEstadoService.TipoFactura,
                Math.Max(maxRegistros * 5, maxRegistros));

            await using var context = await _dbFactory.CreateDbContextAsync();

            if (!idsPendientes.Any())
            {
                var candidatasFallback = await context.Facturas
                    .AsNoTracking()
                    .Where(f =>
                        f.Estado == true &&
                        f.Autorizado == true &&
                        (!string.IsNullOrWhiteSpace(f.Correoad) || !string.IsNullOrWhiteSpace(f.Detalleextra)))
                    .OrderByDescending(f => f.Codfactura)
                    .Select(f => new
                    {
                        f.Codfactura,
                        f.Detalleextra
                    })
                    .Take(Math.Max(maxRegistros * 3, maxRegistros))
                    .ToListAsync();

                var pendientesFallback = candidatasFallback
                    .Where(f => !LeerFacturaCorreoMetadata(f.Detalleextra).CorreoEnviado)
                    .Select(f => f.Codfactura)
                    .Distinct()
                    .ToList();

                foreach (var documentoId in pendientesFallback)
                {
                    await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                        ComprobanteCorreoEstadoService.TipoFactura,
                        documentoId);
                }

                idsPendientes = pendientesFallback;
            }

            if (!idsPendientes.Any())
                return new List<int>();

            var candidatas = await context.Facturas
                .AsNoTracking()
                .Where(f =>
                    idsPendientes.Contains(f.Codfactura) &&
                    f.Estado == true &&
                    f.Autorizado == true)
                .Select(f => new
                {
                    f.Codfactura,
                    f.Detalleextra
                })
                .ToListAsync();

            return candidatas
                .Where(f => !LeerFacturaCorreoMetadata(f.Detalleextra).CorreoEnviado)
                .OrderBy(f => f.Codfactura)
                .Take(Math.Max(1, maxRegistros))
                .Select(f => f.Codfactura)
                .ToList();
        }
        public async Task<bool> AnularFacturaDirectoAsync(int codfactura)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            try
            {
                // Como estás dentro del servicio, aquí SÍ tienes acceso a tu DbContext, 
                // a tu repositorio o a tu conexión de Dapper/SQL.

                // EJEMPLO SI USAS ENTORNO CON REPOSITORIO O CONTEXTO INTERNO:
                // Supongamos que tu contexto interno en el servicio se llama _context o _db:
                var factura = await context.Facturas.FirstOrDefaultAsync(f => f.Codfactura == codfactura);

                if (factura != null)
                {
                    factura.Estado = false; // Cambiamos el campo a false
                    await context.SaveChangesAsync(); // Guardamos en la base de datos
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
       
        #endregion
    }

    }
