using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System.Data;
using System.Globalization;
using System.Xml.Linq;

namespace Simetric.Services
{
    public class GuiaRemisionService
    {
        private const string CodDocGuia = "06";
        private const string CodDocFactura = "01";
        private const string CodigoNumerico = "12345678";
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly IGuiaRemisionPdfService _pdfService;
        private readonly ICajaSerieResolver _cajaSerieResolver;
        private readonly EmisionControlService _emisionControlService;
        private readonly IEmailService _emailService;
        private readonly ComprobanteCorreoEstadoService _comprobanteCorreoEstadoService;
        private readonly InitialSequencePromptService _initialSequencePromptService;
        private readonly SriXmlProcessorService _sriXmlProcessorService;

        public GuiaRemisionService(
            IDbContextFactory<AppDbContext> dbFactory,
            IGuiaRemisionPdfService pdfService,
            ICajaSerieResolver cajaSerieResolver,
            EmisionControlService emisionControlService,
            IEmailService emailService,
            ComprobanteCorreoEstadoService comprobanteCorreoEstadoService,
            InitialSequencePromptService initialSequencePromptService,
            SriXmlProcessorService sriXmlProcessorService)
        {
            _dbFactory = dbFactory;
            _pdfService = pdfService;
            _cajaSerieResolver = cajaSerieResolver;
            _emisionControlService = emisionControlService;
            _emailService = emailService;
            _comprobanteCorreoEstadoService = comprobanteCorreoEstadoService;
            _initialSequencePromptService = initialSequencePromptService;
            _sriXmlProcessorService = sriXmlProcessorService;
        }

        private async Task<CajaSerieResolucion> ResolverSerieGuiaAsync(int userId, string? serieRaw = null)
        {
            if (!string.IsNullOrWhiteSpace(serieRaw))
                return await _cajaSerieResolver.ResolverAsync(userId, serieRaw);

            var resolucionBase = await _cajaSerieResolver.ResolverAsync(userId);
            var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
                userId,
                "guia-remision",
                resolucionBase.SerieRaw);

            if (!string.IsNullOrWhiteSpace(seriePreferida) &&
                !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
            {
                return await _cajaSerieResolver.ResolverAsync(userId, seriePreferida);
            }

            return resolucionBase;
        }

        public async Task<string> GetSerieGuiaVisualAsync(int userId)
        {
            var resolucion = await ResolverSerieGuiaAsync(userId);
            return resolucion.SerieVisual;
        }

        private static async Task<List<int>> ObtenerUsuariosCuentaIdsAsync(AppDbContext context, int idUsuario)
        {
            var usuario = await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == idUsuario)
                .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
                .FirstOrDefaultAsync();

            if (usuario == null)
                return new List<int> { idUsuario };

            var titularId = usuario.estadoAsociado == true && usuario.idJefe is > 0
                ? usuario.idJefe.Value
                : usuario.IdUsuario;

            var usuariosCuenta = await context.Usuarios
                .AsNoTracking()
                .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
                .Select(u => u.IdUsuario)
                .ToListAsync();

            if (usuariosCuenta.Count == 0)
                usuariosCuenta.Add(idUsuario);

            return usuariosCuenta;
        }

        public async Task<Transportista?> GetTransportistaPorIdentificacionAsync(int idUsuario, string numeroIdentificacion)
        {
            numeroIdentificacion = (numeroIdentificacion ?? string.Empty).Trim();
            if (idUsuario <= 0 || string.IsNullOrWhiteSpace(numeroIdentificacion)) return null;

            await using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosCuenta = await ObtenerUsuariosCuentaIdsAsync(context, idUsuario);
            return await context.Transportistas.AsNoTracking()
                .Where(t => context.GuiasRemision.Any(g =>
                    g.IdTranportista == t.Codigo &&
                    g.IdUsuario.HasValue &&
                    usuariosCuenta.Contains(g.IdUsuario.Value)))
                .FirstOrDefaultAsync(t => t.NumeroIdentificacion == numeroIdentificacion);
        }

        public async Task<List<Transportista>> BuscarTransportistasAsync(int idUsuario, string filtro)
        {
            filtro = (filtro ?? string.Empty).Trim().ToLowerInvariant();
            if (idUsuario <= 0 || string.IsNullOrWhiteSpace(filtro)) return new List<Transportista>();

            await using var context = await _dbFactory.CreateDbContextAsync();
            var usuariosCuenta = await ObtenerUsuariosCuentaIdsAsync(context, idUsuario);
            return await context.Transportistas.AsNoTracking()
                .Where(t => context.GuiasRemision.Any(g =>
                    g.IdTranportista == t.Codigo &&
                    g.IdUsuario.HasValue &&
                    usuariosCuenta.Contains(g.IdUsuario.Value)))
                .Where(t => (t.NumeroIdentificacion ?? "").Contains(filtro) || (t.RazonSocial ?? "").ToLower().Contains(filtro))
                .OrderBy(t => t.RazonSocial)
                .Take(10)
                .ToListAsync();
        }

        public async Task<Transportista> GuardarTransportistaAsync(Transportista transportistaData)
        {
            if (transportistaData == null) throw new Exception("Debes ingresar la informacion del transportista.");
            var ident = Limpiar(transportistaData.NumeroIdentificacion);
            if (string.IsNullOrWhiteSpace(ident)) throw new Exception("Debes ingresar la identificacion del transportista.");
            if (string.IsNullOrWhiteSpace(transportistaData.RazonSocial)) throw new Exception("Debes ingresar la razon social del transportista.");
            transportistaData.TipoIdentificacion = ResolverTipoIdentificacionTransportista(
                transportistaData.TipoIdentificacion,
                ident);

            await using var context = await _dbFactory.CreateDbContextAsync();
            var transportistaDb = await context.Transportistas.FirstOrDefaultAsync(t => t.NumeroIdentificacion == ident) ?? new Transportista();
            if (transportistaDb.Codigo == 0)
                context.Transportistas.Add(transportistaDb);

            transportistaDb.RazonSocial = Limpiar(transportistaData.RazonSocial);
            transportistaDb.TipoIdentificacion = Limpiar(transportistaData.TipoIdentificacion);
            transportistaDb.NumeroIdentificacion = ident;
            transportistaDb.Correo = Limpiar(transportistaData.Correo);
            transportistaDb.Placa = Limpiar(transportistaData.Placa);
            transportistaDb.OblCont = Limpiar(transportistaData.OblCont) ?? "NO";
            transportistaDb.ContribuyenteEsp = Limpiar(transportistaData.ContribuyenteEsp);
            transportistaDb.Direccion = Limpiar(transportistaData.Direccion);
            transportistaDb.Telefono = Limpiar(transportistaData.Telefono);

            await context.SaveChangesAsync();
            return transportistaDb;
        }

        public async Task<string> GetNextGuiaRemisionNumeroAsync(int idUsuario, string? serieRaw = null)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var resolucion = await ResolverSerieGuiaAsync(idUsuario, serieRaw);
            var caja = await context.Caja.AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Estado == true &&
                    c.IdUsuario == resolucion.IdUsuario &&
                    c.NumCaja == resolucion.NumeroCaja);

            if (caja == null) throw new Exception("No existe una caja activa para el usuario.");
            var serieNorm = NormalizarSerie(resolucion.SerieVisual);
            if (string.IsNullOrWhiteSpace(serieNorm)) throw new Exception("La caja activa no tiene configurada la serie de guia.");

            var siguiente = await ObtenerSiguienteSecuencialInternoAsync(context, idUsuario, serieNorm);
            return siguiente.ToString().PadLeft(9, '0');
        }

        public async Task<GuiaRemisionGuardadoResultadoDto> GuardarGuiaRemisionCompletaAsync(
            int idUsuario,
            int? codfactura,
            int? codEmisor,
            Transportista transportistaData,
            GuiaRemision guiaData,
            GuiaDestinatario destinatarioData,
            List<DetalleGuiaRemision> detallesData)
        {
            await _emisionControlService.AsegurarPuedeEmitirAsync(idUsuario);

            if (transportistaData == null) throw new Exception("Debes ingresar la informacion del transportista.");
            if (guiaData == null) throw new Exception("Debes ingresar la informacion de la guia.");
            if (destinatarioData == null) throw new Exception("Debes ingresar la informacion del destinatario.");
            if (string.IsNullOrWhiteSpace(transportistaData.NumeroIdentificacion)) throw new Exception("Debes ingresar la identificacion del transportista.");
            if (string.IsNullOrWhiteSpace(transportistaData.RazonSocial)) throw new Exception("Debes ingresar la razon social del transportista.");
            if (detallesData == null || !detallesData.Any()) throw new Exception("La guia debe tener al menos un detalle de traslado.");
            guiaData.Ambiente = 2;
            transportistaData.TipoIdentificacion = ResolverTipoIdentificacionTransportista(
                transportistaData.TipoIdentificacion,
                transportistaData.NumeroIdentificacion);
            ValidarDatosSri(guiaData, destinatarioData, transportistaData, detallesData);

            await using var strategyContext = await _dbFactory.CreateDbContextAsync();
            var strategy = strategyContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                string? rutaXml = null;

                try
                {
                    var facturaDb = codfactura.GetValueOrDefault() > 0
                        ? await context.Facturas.FirstOrDefaultAsync(f => f.Codfactura == codfactura.Value && f.Idusuario == idUsuario)
                        : null;
                    if (codfactura.GetValueOrDefault() > 0 && facturaDb == null)
                        throw new Exception("La factura seleccionada no existe o no pertenece al usuario actual.");
                    if (facturaDb != null &&
                        await context.GuiasRemision.AsNoTracking().AnyAsync(g => g.Codfactura == facturaDb.Codfactura && g.IdUsuario == idUsuario))
                        throw new Exception("La factura seleccionada ya tiene una guia de remision registrada.");

                    var resolucion = await ResolverSerieGuiaAsync(idUsuario, guiaData.Serie);
                    var caja = await context.Caja.FirstOrDefaultAsync(c =>
                        c.Estado == true &&
                        c.IdUsuario == resolucion.IdUsuario &&
                        c.NumCaja == resolucion.NumeroCaja);
                    if (caja == null) throw new Exception("No existe una caja activa para el usuario.");

                    var serieNorm = NormalizarSerie(resolucion.SerieVisual);
                    if (string.IsNullOrWhiteSpace(serieNorm)) throw new Exception("La caja activa no tiene configurada la serie de guia.");

                    var secuencial = ResolverSecuencial(facturaDb?.Guiaremision, guiaData.NumGuiaRemision, serieNorm);
                    if (TryDescomponerNumeroGuia(facturaDb?.Guiaremision, out var serieReservada, out var secReservado))
                    {
                        serieNorm = serieReservada;
                        secuencial = secReservado;
                    }
                    else if (string.IsNullOrWhiteSpace(secuencial))
                    {
                        var siguiente = await ObtenerSiguienteSecuencialInternoAsync(context, idUsuario, serieNorm);
                        secuencial = siguiente.ToString().PadLeft(9, '0');
                    }

                    if (secuencial.Length != 9)
                        throw new Exception("El secuencial de la guia debe tener 9 digitos.");

                    var numeroCompleto = $"{FormatearSerie(serieNorm)}-{secuencial}";
                    var existeNumero = await context.GuiasRemision.AsNoTracking().AnyAsync(g =>
                        g.IdUsuario == idUsuario &&
                        ((g.Serie ?? string.Empty).Replace("-", string.Empty).Trim()) == serieNorm &&
                        (g.NumGuiaRemision ?? string.Empty) == secuencial);
                    if (existeNumero) throw new Exception($"La guia {numeroCompleto} ya existe.");

                    var reservas = await context.Facturas.AsNoTracking()
                        .Where(f => f.Idusuario == idUsuario &&
                                    (!codfactura.HasValue || f.Codfactura != codfactura.Value) &&
                                    f.Guiaremision != null)
                        .Select(f => f.Guiaremision).ToListAsync();
                    if (reservas.Any(x => TryDescomponerNumeroGuia(x, out var s, out var n) && s == serieNorm && n == secuencial))
                        throw new Exception($"La guia {numeroCompleto} ya esta reservada en otra factura.");

                    var ident = transportistaData.NumeroIdentificacion.Trim();
                    var transportistaDb = await context.Transportistas.FirstOrDefaultAsync(t => t.NumeroIdentificacion == ident) ?? new Transportista();
                    if (transportistaDb.Codigo == 0) context.Transportistas.Add(transportistaDb);

                    transportistaDb.RazonSocial = Limpiar(transportistaData.RazonSocial);
                    transportistaDb.TipoIdentificacion = Limpiar(transportistaData.TipoIdentificacion);
                    transportistaDb.NumeroIdentificacion = ident;
                    transportistaDb.Correo = Limpiar(transportistaData.Correo);
                    transportistaDb.Placa = Limpiar(transportistaData.Placa);
                    transportistaDb.OblCont = Limpiar(transportistaData.OblCont);
                    transportistaDb.ContribuyenteEsp = Limpiar(transportistaData.ContribuyenteEsp);
                    transportistaDb.Direccion = Limpiar(transportistaData.Direccion);
                    transportistaDb.Telefono = Limpiar(transportistaData.Telefono);
                    await context.SaveChangesAsync();

                    var codigoEmisor = facturaDb?.Codemisor ?? codEmisor;
                    var emisorDb = codigoEmisor.GetValueOrDefault() > 0
                        ? await context.Emisores.AsNoTracking().FirstOrDefaultAsync(e => e.Codigo == codigoEmisor.Value)
                        : null;
                    if (emisorDb == null)
                        throw new Exception("Debes configurar un emisor activo para generar la guia.");
                    if (string.IsNullOrWhiteSpace(emisorDb.Ruc)) throw new Exception("El emisor asociado no tiene RUC configurado.");
                    ValidarEmisorSri(emisorDb);
                    const int ambiente = 2;
                    var fechaEmision = DateTime.Today;
                    var tipoEmision = string.IsNullOrWhiteSpace(emisorDb.TipoEmision) ? "1" : emisorDb.TipoEmision.Trim();
                    var claveAcceso = GenerarClaveAcceso(
                        fechaEmision,
                        emisorDb.Ruc,
                        ambiente.ToString(CultureInfo.InvariantCulture),
                        serieNorm,
                        secuencial,
                        tipoEmision);

                    var guiaDb = new GuiaRemision
                    {
                        IdTranportista = transportistaDb.Codigo,
                        FechaIniTransporte = guiaData.FechaIniTransporte ?? DateTime.Today,
                        FechaFinTransporte = guiaData.FechaFinTransporte ?? guiaData.FechaIniTransporte ?? DateTime.Today,
                        Placa = Limpiar(guiaData.Placa) ?? transportistaDb.Placa,
                        CodClave = claveAcceso,
                        NumGuiaRemision = secuencial,
                        Fecha = fechaEmision,
                        NumAutorizacion = Limpiar(guiaData.NumAutorizacion),
                        FechaAutorizacion = Limpiar(guiaData.FechaAutorizacion),
                        Mensaje = Limpiar(guiaData.Mensaje),
                        IdEmpresa = caja.IdEmpresa ?? emisorDb.IdEmpresa,
                        IdSucursal = caja.IdSucursal ?? emisorDb.IdSucursal,
                        EstadoSRI = "P",
                        IdUsuario = idUsuario,
                        Serie = serieNorm,
                        Ambiente = ambiente,
                        Codfactura = facturaDb?.Codfactura,
                        DireccionPartida = Limpiar(guiaData.DireccionPartida)
                    };
                    context.GuiasRemision.Add(guiaDb);
                    await context.SaveChangesAsync();

                    var destinatarioDb = new GuiaDestinatario
                    {
                        IdGuiaRemision = guiaDb.Sec,
                        IdDestinatario = Limpiar(destinatarioData.IdDestinatario),
                        RazonSocial = Limpiar(destinatarioData.RazonSocial),
                        Direccion = Limpiar(destinatarioData.Direccion),
                        DocAduanero = Limpiar(destinatarioData.DocAduanero),
                        CodEstablecimiento = Limpiar(destinatarioData.CodEstablecimiento),
                        Ruta = Limpiar(destinatarioData.Ruta),
                        MotivoTraslado = Limpiar(destinatarioData.MotivoTraslado) ?? "VENTA",
                        CodDocSustento = facturaDb != null ? CodDocFactura : null,
                        NumDocSustento = facturaDb != null
                            ? Limpiar(destinatarioData.NumDocSustento) ?? facturaDb.Numfactura
                            : null,
                        NumAutorizacionSustento = facturaDb != null
                            ? Limpiar(destinatarioData.NumAutorizacionSustento) ?? facturaDb.Numautorizacion
                            : null,
                        FechaEmiSustento = facturaDb != null
                            ? destinatarioData.FechaEmiSustento ?? facturaDb.Fechaentrega ?? DateTime.Today
                            : null,
                        SerieDocSustento = facturaDb != null
                            ? Limpiar(destinatarioData.SerieDocSustento) ?? FormatearSerie(facturaDb.Serie)
                            : null
                    };
                    ValidarDocumentoSustentoSri(destinatarioDb);
                    context.GuiaDestinatarios.Add(destinatarioDb);

                    var detallesDb = new List<DetalleGuiaRemision>();
                    foreach (var item in detallesData)
                    {
                        if ((item.Cantidad ?? 0) <= 0) throw new Exception("Todas las cantidades de la guia deben ser mayores a cero.");
                        var detalleDb = new DetalleGuiaRemision
                        {
                            IdGuiaRemision = guiaDb.Sec,
                            CodInterno = Limpiar(item.CodInterno),
                            CodAdicional = Limpiar(item.CodAdicional),
                            Descripcion = Limpiar(item.Descripcion),
                            Cantidad = item.Cantidad
                        };
                        detallesDb.Add(detalleDb);
                        context.DetallesGuiaRemision.Add(detalleDb);
                    }

                    if (facturaDb != null)
                    {
                        facturaDb.Guiaremision = numeroCompleto;
                        facturaDb.Codtransportista = transportistaDb.Codigo;
                    }
                    await context.SaveChangesAsync();
                    await _emisionControlService.ConsumirDocumentoAsync(context, idUsuario);

                    var xml = GenerarXml(guiaDb, destinatarioDb, detallesDb, transportistaDb, emisorDb);
                    rutaXml = await GuardarXmlAsync(xml, ConstruirNombreArchivo(emisorDb.Ruc, serieNorm, secuencial));

                    await transaction.CommitAsync();
                    return new GuiaRemisionGuardadoResultadoDto
                    {
                        SecGuiaRemision = guiaDb.Sec,
                        CodigoTransportista = transportistaDb.Codigo,
                        Codfactura = facturaDb?.Codfactura,
                        Serie = FormatearSerie(serieNorm),
                        Secuencial = secuencial,
                        NumeroCompleto = numeroCompleto,
                        RutaXml = rutaXml,
                        NombreArchivoXml = Path.GetFileName(rutaXml),
                        RutaPdf = ConstruirPdfRutaLocal(emisorDb.Ruc, serieNorm, secuencial),
                        NombreArchivoPdf = ConstruirNombreArchivoPdf(emisorDb.Ruc, serieNorm, secuencial),
                        ClaveAcceso = claveAcceso
                    };
                }
                catch
                {
                    try { await transaction.RollbackAsync(); } catch { }
                    if (!string.IsNullOrWhiteSpace(rutaXml) && File.Exists(rutaXml)) { try { File.Delete(rutaXml); } catch { } }
                    throw;
                }
            });
        }

        public async Task<mensajeSRI> EmitirGuiaRemisionSriAsync(
            int sec,
            int? idUsuario = null,
            bool intentarEnviarCorreo = true)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var guia = await context.GuiasRemision.FirstOrDefaultAsync(g => g.Sec == sec);
            if (guia == null)
                return CrearErrorSri("No se encontro la guia de remision para enviar al SRI.");

            if (idUsuario.HasValue && guia.IdUsuario != idUsuario.Value)
                return CrearErrorSri("La guia de remision no pertenece al usuario actual.");
            if (GuiaRemisionEstaAnulada(guia.EstadoSRI))
                return CrearErrorSri("La guia de remision esta anulada y ya no puede reenviarse al SRI.");

            if (GuiaRemisionEstaAutorizada(guia.EstadoSRI))
            {
                if (intentarEnviarCorreo)
                    await IntentarEnviarGuiaRemisionPorCorreoAsync(sec);

                return new mensajeSRI
                {
                    estado = DocumentoAutorizacionHelper.EstadoAutorizado,
                    autorizacion = guia.NumAutorizacion ?? string.Empty,
                    fecha = guia.FechaAutorizacion ?? string.Empty,
                    mensaje = "La guia de remision ya se encuentra autorizada."
                };
            }

            var fechaReenvioActualizada = ComprobanteReenvioFechaHelper.PuedeRenovarFecha(guia.EstadoSRI, guia.Mensaje) &&
                                          ComprobanteReenvioFechaHelper.DebeActualizar(guia.Fecha, guia.CodClave);
            if (fechaReenvioActualizada)
            {
                var fechaAnterior = guia.Fecha ?? DateTime.Today;
                guia.FechaIniTransporte = ComprobanteReenvioFechaHelper.DesplazarFecha(guia.FechaIniTransporte, fechaAnterior);
                guia.FechaFinTransporte = ComprobanteReenvioFechaHelper.DesplazarFecha(guia.FechaFinTransporte, fechaAnterior);
                guia.Fecha = DateTime.Today;
                guia.CodClave = null;
                guia.NumAutorizacion = null;
                guia.FechaAutorizacion = null;
            }

            var detalle = await GetGuiaRemisionDetalleAsync(sec);
            if (detalle?.Emisor == null)
                return await RegistrarErrorSriAsync(context, guia, "No se encontro el emisor asociado a la guia de remision.");

            var emisor = detalle.Emisor;
            if (string.IsNullOrWhiteSpace(emisor.PathCertificado) || string.IsNullOrWhiteSpace(emisor.ClaveCertificado))
                return await RegistrarErrorSriAsync(context, guia, "El emisor no tiene configurado el certificado electronico requerido para enviar la guia al SRI.");

            if (fechaReenvioActualizada)
            {
                guia.CodClave = GenerarClaveAcceso(
                    DateTime.Today,
                    emisor.Ruc,
                    "2",
                    guia.Serie,
                    guia.NumGuiaRemision,
                    string.IsNullOrWhiteSpace(emisor.TipoEmision) ? "1" : emisor.TipoEmision.Trim());
                await context.SaveChangesAsync();
            }

            var xmlUrl = await AsegurarXmlGuiaRemisionAsync(sec);
            var rutaXml = ConstruirXmlRutaLocal(emisor.Ruc ?? string.Empty, guia.Serie, guia.NumGuiaRemision);
            if (string.IsNullOrWhiteSpace(xmlUrl) || !File.Exists(rutaXml))
                return await RegistrarErrorSriAsync(context, guia, "No se pudo generar el XML de la guia de remision para enviarlo al SRI.");

            var respuesta = await _sriXmlProcessorService.ProcessXmlAsync(
                rutaXml,
                emisor.PathCertificado,
                emisor.ClaveCertificado);

            var autorizada = string.Equals(
                respuesta.estado,
                DocumentoAutorizacionHelper.EstadoAutorizado,
                StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(respuesta.autorizacion);

            guia.EstadoSRI = autorizada ? "A" : "N";
            guia.NumAutorizacion = string.IsNullOrWhiteSpace(respuesta.autorizacion)
                ? guia.NumAutorizacion
                : respuesta.autorizacion.Trim();
            guia.FechaAutorizacion = string.IsNullOrWhiteSpace(respuesta.fecha)
                ? DateTime.Now.ToString("O")
                : respuesta.fecha;
            guia.Mensaje = autorizada
                ? "ok"
                : string.IsNullOrWhiteSpace(respuesta.mensaje) ? respuesta.estado : respuesta.mensaje;
            await context.SaveChangesAsync();

            if (autorizada)
            {
                await AsegurarPdfGuiaRemisionAsync(sec);
                if (intentarEnviarCorreo)
                    await IntentarEnviarGuiaRemisionPorCorreoAsync(sec, rutaXml);
            }

            return respuesta;
        }

        public async Task<List<GuiaRemisionListDto>> ListarGuiasRemisionUsuarioAsync(int idUsuario)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var data = await (
                from g in db.GuiasRemision.AsNoTracking()
                join gd in db.GuiaDestinatarios.AsNoTracking()
                    on g.Sec equals gd.IdGuiaRemision into destJoin
                from gd in destJoin.DefaultIfEmpty()

                join t in db.Transportistas.AsNoTracking()
                    on g.IdTranportista equals t.Codigo into trJoin
                from t in trJoin.DefaultIfEmpty()

                join f in db.Facturas.AsNoTracking()
                    on g.Codfactura equals f.Codfactura into facJoin
                from f in facJoin.DefaultIfEmpty()

                join e in db.Emisores.AsNoTracking()
                    on f.Codemisor equals e.Codigo into emiJoin
                from e in emiJoin.DefaultIfEmpty()

                where g.IdUsuario == idUsuario &&
                      (e == null || e.EsEmisorSistema != true) &&
                      (g.EstadoSRI == null || g.EstadoSRI != "ANULADA")
                orderby g.Sec descending
                select new
                {
                    g.Sec,
                    g.NumGuiaRemision,
                    g.Serie,
                    g.Fecha,
                    g.FechaIniTransporte,
                    g.FechaFinTransporte,
                    g.EstadoSRI,
                    g.NumAutorizacion,
                    g.FechaAutorizacion,
                    g.CodClave,
                    g.IdEmpresa,
                    g.IdSucursal,
                    g.IdUsuario,
                    Destinatario = gd != null ? gd.RazonSocial : null,
                    IdentificacionDestinatario = gd != null ? gd.IdDestinatario : null,
                    SerieDocSustento = gd != null ? gd.SerieDocSustento : null,
                    NumDocSustento = gd != null ? gd.NumDocSustento : null,
                    MotivoTraslado = gd != null ? gd.MotivoTraslado : null,
                    Transportista = t != null ? t.RazonSocial : null,
                    EmisorRuc = e != null ? e.Ruc : null
                })
                .ToListAsync();

            var empresas = data
                .Where(x => x.IdEmpresa.HasValue)
                .Select(x => x.IdEmpresa!.Value)
                .Distinct()
                .ToList();
            var emisoresSede = await db.Emisores.AsNoTracking()
                .Where(e => e.Estado && e.IdEmpresa.HasValue && empresas.Contains(e.IdEmpresa.Value))
                .Select(e => new { e.IdEmpresa, e.IdSucursal, e.IdUsuario, e.EsEmisorSistema, e.Ruc })
                .ToListAsync();

            return data.Select(x =>
            {
                var ruc = x.EmisorRuc;
                if (string.IsNullOrWhiteSpace(ruc))
                {
                    ruc = emisoresSede
                        .Where(e => e.IdEmpresa == x.IdEmpresa && e.IdSucursal == x.IdSucursal)
                        .OrderByDescending(e => e.IdUsuario == x.IdUsuario)
                        .ThenByDescending(e => e.EsEmisorSistema)
                        .Select(e => e.Ruc)
                        .FirstOrDefault();
                }

                return new GuiaRemisionListDto
                {
                    Sec = x.Sec,
                    NumeroGuiaRemision = x.NumGuiaRemision ?? "",
                    Serie = x.Serie ?? "",
                    FechaEmision = x.Fecha,
                    FechaInicioTransporte = x.FechaIniTransporte,
                    FechaFinTransporte = x.FechaFinTransporte,
                    Destinatario = x.Destinatario ?? "",
                    IdentificacionDestinatario = x.IdentificacionDestinatario ?? "",
                    Transportista = x.Transportista ?? "",
                    FacturaSustento = FormatearNumeroDocumento(x.SerieDocSustento, x.NumDocSustento),
                    MotivoTraslado = x.MotivoTraslado ?? "",
                    EstadoSri = x.EstadoSRI ?? "",
                    NumeroAutorizacion = x.NumAutorizacion ?? "",
                    FechaAutorizacion = x.FechaAutorizacion ?? "",
                    ClaveAcceso = x.CodClave ?? "",
                    XmlUrl = !string.IsNullOrWhiteSpace(ruc)
                        ? ConstruirXmlUrl(ruc, x.Serie, x.NumGuiaRemision)
                        : "",
                    PdfUrl = !string.IsNullOrWhiteSpace(ruc)
                        ? ConstruirPdfUrl(ruc, x.Serie, x.NumGuiaRemision)
                        : ""
                };
            }).ToList();
        }

        public async Task<GuiaRemisionDetalleViewDto?> GetGuiaRemisionDetalleUsuarioAsync(int sec, int idUsuario)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existe = await db.GuiasRemision.AsNoTracking().AnyAsync(x => x.Sec == sec && x.IdUsuario == idUsuario);
            if (!existe)
                return null;

            return await GetGuiaRemisionDetalleAsync(sec);
        }

        public async Task<string?> AsegurarXmlGuiaRemisionUsuarioAsync(int sec, int idUsuario)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existe = await db.GuiasRemision.AsNoTracking().AnyAsync(x => x.Sec == sec && x.IdUsuario == idUsuario);
            if (!existe)
                return null;

            return await AsegurarXmlGuiaRemisionAsync(sec);
        }

        public async Task<string?> AsegurarPdfGuiaRemisionUsuarioAsync(int sec, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existe = await db.GuiasRemision.AsNoTracking().AnyAsync(x => x.Sec == sec && x.IdUsuario == idUsuario);
            if (!existe)
                return null;

            return await AsegurarPdfGuiaRemisionAsync(sec, formato);
        }

        private static async Task<long> ObtenerSiguienteSecuencialInternoAsync(AppDbContext context, int idUsuario, string serieNorm)
        {
            var maximo = 0L;
            var existentes = await context.GuiasRemision.AsNoTracking().Where(g => g.IdUsuario == idUsuario)
                .Select(g => new { g.Serie, g.NumGuiaRemision }).ToListAsync();
            foreach (var item in existentes)
            {
                if (NormalizarSerie(item.Serie) != serieNorm) continue;
                if (long.TryParse(SoloDigitos(item.NumGuiaRemision), out var num) && num > maximo) maximo = num;
            }

            var reservas = await context.Facturas.AsNoTracking().Where(f => f.Idusuario == idUsuario && f.Guiaremision != null)
                .Select(f => f.Guiaremision).ToListAsync();
            foreach (var item in reservas)
            {
                if (!TryDescomponerNumeroGuia(item, out var serie, out var sec)) continue;
                if (serie != serieNorm) continue;
                if (long.TryParse(sec, out var num) && num > maximo) maximo = num;
            }
            return maximo + 1;
        }

        private async Task<GuiaRemisionDetalleViewDto?> GetGuiaRemisionDetalleAsync(int sec)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var raw = await (
                from g in db.GuiasRemision.AsNoTracking()
                join gd in db.GuiaDestinatarios.AsNoTracking()
                    on g.Sec equals gd.IdGuiaRemision into destJoin
                from gd in destJoin.DefaultIfEmpty()

                join t in db.Transportistas.AsNoTracking()
                    on g.IdTranportista equals t.Codigo into trJoin
                from t in trJoin.DefaultIfEmpty()

                join f in db.Facturas.AsNoTracking()
                    on g.Codfactura equals f.Codfactura into facJoin
                from f in facJoin.DefaultIfEmpty()

                join e in db.Emisores.AsNoTracking()
                    on f.Codemisor equals e.Codigo into emiJoin
                from e in emiJoin.DefaultIfEmpty()

                where g.Sec == sec
                select new
                {
                    Guia = g,
                    Destinatario = gd,
                    Transportista = t,
                    Factura = f,
                    Emisor = e
                })
                .FirstOrDefaultAsync();

            if (raw == null)
                return null;

            var emisor = raw.Emisor ?? await ResolverEmisorGuiaAsync(db, raw.Guia, raw.Factura);

            var detalles = await db.DetallesGuiaRemision.AsNoTracking()
                .Where(d => d.IdGuiaRemision == sec)
                .OrderBy(d => d.Sec)
                .Select(d => new GuiaRemisionDetalleLineaDto
                {
                    CodigoInterno = d.CodInterno ?? "",
                    CodigoAdicional = d.CodAdicional ?? "",
                    Descripcion = d.Descripcion ?? "",
                    Cantidad = d.Cantidad ?? 0m
                })
                .ToListAsync();

            return new GuiaRemisionDetalleViewDto
            {
                Guia = raw.Guia,
                Destinatario = raw.Destinatario,
                Transportista = raw.Transportista,
                Factura = raw.Factura,
                Emisor = emisor,
                NumeroCompleto = FormatearNumeroCompleto(raw.Guia.Serie, raw.Guia.NumGuiaRemision),
                NumeroDocumentoSustentoVisual = FormatearNumeroDocumento(raw.Destinatario?.SerieDocSustento, raw.Destinatario?.NumDocSustento),
                XmlUrl = !string.IsNullOrWhiteSpace(emisor?.Ruc)
                    ? ConstruirXmlUrl(emisor.Ruc, raw.Guia.Serie, raw.Guia.NumGuiaRemision)
                    : "",
                PdfUrl = !string.IsNullOrWhiteSpace(emisor?.Ruc)
                    ? ConstruirPdfUrl(emisor.Ruc, raw.Guia.Serie, raw.Guia.NumGuiaRemision)
                    : "",
                Detalles = detalles
            };
        }

        private async Task<string?> AsegurarXmlGuiaRemisionAsync(int sec)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var raw = await (
                from g in db.GuiasRemision.AsNoTracking()
                join gd in db.GuiaDestinatarios.AsNoTracking()
                    on g.Sec equals gd.IdGuiaRemision into destJoin
                from gd in destJoin.DefaultIfEmpty()

                join t in db.Transportistas.AsNoTracking()
                    on g.IdTranportista equals t.Codigo into trJoin
                from t in trJoin.DefaultIfEmpty()

                join f in db.Facturas.AsNoTracking()
                    on g.Codfactura equals f.Codfactura into facJoin
                from f in facJoin.DefaultIfEmpty()

                join e in db.Emisores.AsNoTracking()
                    on f.Codemisor equals e.Codigo into emiJoin
                from e in emiJoin.DefaultIfEmpty()

                where g.Sec == sec
                select new
                {
                    Guia = g,
                    Destinatario = gd,
                    Transportista = t,
                    Emisor = e
                })
                .FirstOrDefaultAsync();

            if (raw?.Guia == null || raw.Destinatario == null || raw.Transportista == null)
                return null;

            var emisor = raw.Emisor ?? await ResolverEmisorGuiaAsync(db, raw.Guia, null);
            if (emisor == null || string.IsNullOrWhiteSpace(emisor.Ruc))
                return null;

            if (string.IsNullOrWhiteSpace(raw.Guia.CodClave))
            {
                raw.Guia.CodClave = GenerarClaveAcceso(
                    raw.Guia.Fecha ?? raw.Guia.FechaIniTransporte ?? DateTime.Now,
                    emisor.Ruc,
                    "2",
                    raw.Guia.Serie,
                    raw.Guia.NumGuiaRemision,
                    string.IsNullOrWhiteSpace(emisor.TipoEmision) ? "1" : emisor.TipoEmision.Trim());
            }

            var detalles = await db.DetallesGuiaRemision.AsNoTracking()
                .Where(d => d.IdGuiaRemision == sec)
                .OrderBy(d => d.Sec)
                .ToListAsync();

            var xml = GenerarXml(raw.Guia, raw.Destinatario, detalles, raw.Transportista, emisor);
            await GuardarXmlAsync(xml, ConstruirNombreArchivo(emisor.Ruc, raw.Guia.Serie, raw.Guia.NumGuiaRemision));

            return ConstruirXmlUrl(emisor.Ruc, raw.Guia.Serie, raw.Guia.NumGuiaRemision);
        }

        private async Task<string?> AsegurarPdfGuiaRemisionAsync(int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
        {
            var detalle = await GetGuiaRemisionDetalleAsync(sec);
            if (detalle?.Guia == null || detalle.Emisor == null || string.IsNullOrWhiteSpace(detalle.Emisor.Ruc))
                return null;

            var rutaLocal = ConstruirPdfRutaLocal(detalle.Emisor.Ruc, detalle.Guia.Serie, detalle.Guia.NumGuiaRemision, formato);
            if (!File.Exists(rutaLocal))
            {
                await _pdfService.GenerarPdfGuiaRemisionAsync(detalle, formato);
            }

            return ConstruirPdfUrl(detalle.Emisor.Ruc, detalle.Guia.Serie, detalle.Guia.NumGuiaRemision, formato);
        }

        public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarGuiaRemisionPorCorreoAsync(
            int sec,
            string? rutaXmlExistente = null,
            string? rutaPdfExistente = null,
            bool forzarReenvio = false)
        {
            var detalle = await GetGuiaRemisionDetalleAsync(sec);
            if (detalle?.Guia == null)
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    Mensaje = "No se encontró la guía de remisión para enviar por correo."
                };
            }

            if (GuiaRemisionEstaAnulada(detalle.Guia.EstadoSRI))
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    Mensaje = "La guÃ­a de remisiÃ³n estÃ¡ anulada y ya no puede reenviarse por correo."
                };
            }

            var seguimiento = await _comprobanteCorreoEstadoService.GetEstadoAsync(
                ComprobanteCorreoEstadoService.TipoGuiaRemision,
                sec);
            if (!forzarReenvio && seguimiento?.CorreoEnviado == true)
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    YaEnviado = true,
                    Mensaje = "El correo de esta guía de remisión ya fue enviado anteriormente.",
                    TotalDestinatarios = 0
                };
            }

            await using var context = await _dbFactory.CreateDbContextAsync();

            Cliente? cliente = null;
            if (detalle.Factura?.Codclientes is > 0)
            {
                cliente = await context.Clientes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Codcliente == detalle.Factura.Codclientes.Value);
            }

            var destinatarios = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
                context,
                detalle.Guia.IdUsuario,
                detalle.Factura?.Codclientes,
                cliente?.Correo);

            if (!destinatarios.Any())
            {
                return new FacturaCorreoEnvioResultadoDto
                {
                    SinDestinatarios = true,
                    Mensaje = "La guía de remisión no tiene correos configurados para el envío."
                };
            }

            if (!GuiaRemisionEstaAutorizada(detalle.Guia.EstadoSRI))
            {
                await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec);

                return new FacturaCorreoEnvioResultadoDto
                {
                    PendienteAutorizacion = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"La guía de remisión aún no está autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s) hasta que EstadoSRI indique AUTORIZADO."
                };
            }

            if (detalle.Emisor == null || string.IsNullOrWhiteSpace(detalle.Emisor.Ruc))
            {
                await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec,
                    "No se pudo identificar el emisor de la guía de remisión para enviar el correo.");

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = "No se pudo identificar el emisor de la guía de remisión para enviar el correo."
                };
            }

            var rutaXml = rutaXmlExistente;
            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            {
                rutaXml = ConstruirXmlRutaLocal(detalle.Emisor.Ruc, detalle.Guia.Serie, detalle.Guia.NumGuiaRemision);
                if (!File.Exists(rutaXml))
                {
                    await AsegurarXmlGuiaRemisionAsync(sec);
                    rutaXml = ConstruirXmlRutaLocal(detalle.Emisor.Ruc, detalle.Guia.Serie, detalle.Guia.NumGuiaRemision);
                }
            }

            if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            {
                await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec,
                    "No se pudo generar o ubicar el XML adjunto para enviar la guía de remisión por correo.");

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = "No se pudo generar o ubicar el XML adjunto para enviar la guía de remisión por correo."
                };
            }

            var rutaPdf = rutaPdfExistente;
            if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
            {
                rutaPdf = ConstruirPdfRutaLocal(detalle.Emisor.Ruc, detalle.Guia.Serie, detalle.Guia.NumGuiaRemision);
                if (!File.Exists(rutaPdf))
                    rutaPdf = await _pdfService.GenerarPdfGuiaRemisionAsync(detalle);
            }

            if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
            {
                await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec,
                    "No se pudo generar o ubicar el PDF adjunto para enviar la guía de remisión por correo.");

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = "No se pudo generar o ubicar el PDF adjunto para enviar la guía de remisión por correo."
                };
            }

            try
            {
                await _emailService.EnviarGuiaRemisionAsync(
                    detalle.NumeroCompleto,
                    detalle.NumeroDocumentoSustentoVisual,
                    destinatarios,
                    detalle.Destinatario?.RazonSocial,
                    rutaXml,
                    rutaPdf);

                await _comprobanteCorreoEstadoService.MarcarEnviadoAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec);

                return new FacturaCorreoEnvioResultadoDto
                {
                    Enviado = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"Se envió el correo con XML y PDF de la guía de remisión a {destinatarios.Count} destinatario(s)."
                };
            }
            catch (Exception ex)
            {
                await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                    ComprobanteCorreoEstadoService.TipoGuiaRemision,
                    sec,
                    ex.Message);

                return new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"No se pudo enviar el correo de la guía de remisión: {ex.Message}"
                };
            }
        }

        public async Task<List<int>> GetGuiasRemisionAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
        {
            var idsPendientes = await _comprobanteCorreoEstadoService.GetDocumentosPendientesAsync(
                ComprobanteCorreoEstadoService.TipoGuiaRemision,
                Math.Max(maxRegistros * 5, maxRegistros));

            if (!idsPendientes.Any())
                return new List<int>();

            await using var db = await _dbFactory.CreateDbContextAsync();
            var candidatos = await db.GuiasRemision
                .AsNoTracking()
                .Where(g => idsPendientes.Contains(g.Sec))
                .Select(g => new { g.Sec, g.EstadoSRI })
                .ToListAsync();

            return candidatos
                .Where(g => !GuiaRemisionEstaAnulada(g.EstadoSRI) && GuiaRemisionEstaAutorizada(g.EstadoSRI))
                .OrderBy(g => g.Sec)
                .Take(maxRegistros)
                .Select(g => g.Sec)
                .ToList();
        }

        public async Task<bool> AnularGuiaRemisionDirectoAsync(int sec, int? idUsuario = null)
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            try
            {
                var guia = await context.GuiasRemision.FirstOrDefaultAsync(g =>
                    g.Sec == sec &&
                    (!idUsuario.HasValue || g.IdUsuario == idUsuario.Value));

                if (guia == null)
                    return false;

                guia.EstadoSRI = "ANULADA";
                guia.Mensaje = "ANULADA";
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static XDocument GenerarXml(GuiaRemision guia, GuiaDestinatario destinatario, IEnumerable<DetalleGuiaRemision> detalles, Transportista transportista, Emisor emisor)
        {
            var c = CultureInfo.InvariantCulture;
            var serie = NormalizarSerie(guia.Serie);
            var tieneDocumentoSustento =
                !string.IsNullOrWhiteSpace(destinatario.NumDocSustento) &&
                !string.IsNullOrWhiteSpace(destinatario.SerieDocSustento);
            var infoAdicional = new List<XElement>();
            if (!string.IsNullOrWhiteSpace(transportista.Telefono)) infoAdicional.Add(new XElement("campoAdicional", new XAttribute("nombre", "telefono"), transportista.Telefono.Trim()));
            if (!string.IsNullOrWhiteSpace(transportista.Correo)) infoAdicional.Add(new XElement("campoAdicional", new XAttribute("nombre", "email"), transportista.Correo.Trim()));

            return new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("guiaRemision",
                    new XAttribute("id", "comprobante"),
                    new XAttribute("version", "1.1.0"),
                    new XElement("infoTributaria",
                        new XElement("ambiente", "2"),
                        new XElement("tipoEmision", string.IsNullOrWhiteSpace(emisor.TipoEmision) ? "1" : emisor.TipoEmision.Trim()),
                        new XElement("razonSocial", Limpiar(emisor.RazonSocial) ?? string.Empty),
                        !string.IsNullOrWhiteSpace(emisor.NomComercial)
                            ? new XElement("nombreComercial", emisor.NomComercial.Trim())
                            : null,
                        new XElement("ruc", Limpiar(emisor.Ruc) ?? string.Empty),
                        new XElement("claveAcceso", Limpiar(guia.CodClave) ?? string.Empty),
                        new XElement("codDoc", CodDocGuia),
                        new XElement("estab", serie.Length >= 3 ? serie[..3] : string.Empty),
                        new XElement("ptoEmi", serie.Length >= 6 ? serie.Substring(3, 3) : string.Empty),
                        new XElement("secuencial", SoloDigitos(guia.NumGuiaRemision).PadLeft(9, '0')),
                        new XElement("dirMatriz", Limpiar(emisor.DireccionMatriz) ?? string.Empty)
                    ),
                    new XElement("infoGuiaRemision",
                        new XElement("dirEstablecimiento", Limpiar(emisor.DirEstablecimiento) ?? Limpiar(emisor.DireccionMatriz) ?? string.Empty),
                        new XElement("dirPartida", Limpiar(guia.DireccionPartida) ?? string.Empty),
                        new XElement("razonSocialTransportista", Limpiar(transportista.RazonSocial) ?? string.Empty),
                        new XElement("tipoIdentificacionTransportista", Limpiar(transportista.TipoIdentificacion) ?? string.Empty),
                        new XElement("rucTransportista", Limpiar(transportista.NumeroIdentificacion) ?? string.Empty),
                        new XElement("obligadoContabilidad", NormalizarSiNo(transportista.OblCont) ?? NormalizarSiNo(emisor.LlevaContabilidad) ?? "NO"),
                        !string.IsNullOrWhiteSpace(transportista.ContribuyenteEsp)
                            ? new XElement("contribuyenteEspecial", transportista.ContribuyenteEsp.Trim())
                            : null,
                        new XElement("fechaIniTransporte", (guia.FechaIniTransporte ?? guia.Fecha ?? DateTime.Today).ToString("dd/MM/yyyy", c)),
                        new XElement("fechaFinTransporte", (guia.FechaFinTransporte ?? guia.FechaIniTransporte ?? guia.Fecha ?? DateTime.Today).ToString("dd/MM/yyyy", c)),
                        new XElement("placa", Limpiar(guia.Placa) ?? Limpiar(transportista.Placa) ?? string.Empty)
                    ),
                    new XElement("destinatarios",
                        new XElement("destinatario",
                            new XElement("identificacionDestinatario", Limpiar(destinatario.IdDestinatario) ?? string.Empty),
                            new XElement("razonSocialDestinatario", Limpiar(destinatario.RazonSocial) ?? string.Empty),
                            new XElement("dirDestinatario", Limpiar(destinatario.Direccion) ?? string.Empty),
                            new XElement("motivoTraslado", Limpiar(destinatario.MotivoTraslado) ?? "VENTA"),
                            !string.IsNullOrWhiteSpace(destinatario.DocAduanero) ? new XElement("docAduaneroUnico", destinatario.DocAduanero.Trim()) : null,
                            !string.IsNullOrWhiteSpace(destinatario.CodEstablecimiento) ? new XElement("codEstabDestino", destinatario.CodEstablecimiento.Trim()) : null,
                            !string.IsNullOrWhiteSpace(destinatario.Ruta) ? new XElement("ruta", destinatario.Ruta.Trim()) : null,
                            tieneDocumentoSustento ? new XElement("codDocSustento", Limpiar(destinatario.CodDocSustento) ?? CodDocFactura) : null,
                            tieneDocumentoSustento ? new XElement("numDocSustento", FormatearNumeroDocumento(destinatario.SerieDocSustento, destinatario.NumDocSustento)) : null,
                            tieneDocumentoSustento && !string.IsNullOrWhiteSpace(destinatario.NumAutorizacionSustento)
                                ? new XElement("numAutDocSustento", destinatario.NumAutorizacionSustento.Trim())
                                : null,
                            tieneDocumentoSustento ? new XElement("fechaEmisionDocSustento", (destinatario.FechaEmiSustento ?? DateTime.Today).ToString("dd/MM/yyyy", c)) : null,
                            new XElement("detalles",
                                detalles.Select(d => new XElement("detalle",
                                    !string.IsNullOrWhiteSpace(d.CodInterno) ? new XElement("codigoInterno", d.CodInterno.Trim()) : null,
                                    !string.IsNullOrWhiteSpace(d.CodAdicional) ? new XElement("codigoAdicional", d.CodAdicional.Trim()) : null,
                                    new XElement("descripcion", Limpiar(d.Descripcion) ?? string.Empty),
                                    new XElement("cantidad", (d.Cantidad ?? 0).ToString("0.######", c))
                                )))
                        )
                    ),
                    infoAdicional.Any() ? new XElement("infoAdicional", infoAdicional) : null
                ));
        }

        private static async Task<string> GuardarXmlAsync(XDocument documento, string nombreArchivo)
        {
            var carpeta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "comprobantes", "guias_remision");
            if (!Directory.Exists(carpeta)) Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, nombreArchivo);
            await Task.Run(() => documento.Save(ruta));
            return ruta;
        }

        private static string ConstruirXmlUrl(string ruc, string? serie, string? secuencial)
            => $"/comprobantes/guias_remision/{ConstruirNombreArchivo(ruc, serie, secuencial)}";

        private static string ConstruirXmlRutaLocal(string ruc, string? serie, string? secuencial)
            => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "comprobantes", "guias_remision", ConstruirNombreArchivo(ruc, serie, secuencial));

        private static string ConstruirNombreArchivo(string? ruc, string? serie, string? secuencial)
            => $"{(Limpiar(ruc) ?? "SIN_RUC")}_06_{NormalizarSerie(serie).PadLeft(6, '0')}{SoloDigitos(secuencial).PadLeft(9, '0')}.xml";

        private static string ConstruirPdfUrl(string ruc, string? serie, string? secuencial, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
            => $"/comprobantes/guias_remision/{ConstruirNombreArchivoPdf(ruc, serie, secuencial, formato)}";

        private static string ConstruirPdfRutaLocal(string ruc, string? serie, string? secuencial, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
            => Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "comprobantes", "guias_remision", ConstruirNombreArchivoPdf(ruc, serie, secuencial, formato));

        private static string ConstruirNombreArchivoPdf(string? ruc, string? serie, string? secuencial, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
            => $"{(Limpiar(ruc) ?? "SIN_RUC")}_06_{NormalizarSerie(serie).PadLeft(6, '0')}{SoloDigitos(secuencial).PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";

        private static bool GuiaRemisionEstaAutorizada(string? estadoSri)
            => DocumentoAutorizacionHelper.EstaAutorizado(estadoSri);

        private static bool GuiaRemisionEstaAnulada(string? estadoSri)
            => string.Equals((estadoSri ?? string.Empty).Trim(), "ANULADA", StringComparison.OrdinalIgnoreCase);

        private static string GenerarClaveAcceso(DateTime fecha, string? ruc, string ambiente, string? serie, string? secuencial, string tipoEmision)
        {
            var base48 =
                fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture) +
                CodDocGuia +
                (Limpiar(ruc) ?? string.Empty).PadLeft(13, '0') +
                ambiente.Trim().PadLeft(1, '0') +
                NormalizarSerie(serie).PadLeft(6, '0') +
                SoloDigitos(secuencial).PadLeft(9, '0') +
                CodigoNumerico +
                (string.IsNullOrWhiteSpace(tipoEmision) ? "1" : tipoEmision.Trim()).PadLeft(1, '0');

            var suma = 0;
            var factor = 2;
            for (var i = base48.Length - 1; i >= 0; i--)
            {
                suma += (int)char.GetNumericValue(base48[i]) * factor;
                factor = factor == 7 ? 2 : factor + 1;
            }
            var verificador = 11 - (suma % 11);
            if (verificador == 11) verificador = 0;
            if (verificador == 10) verificador = 1;
            return base48 + verificador.ToString(CultureInfo.InvariantCulture);
        }

        private static string ResolverSecuencial(string? numeroReserva, string? numeroManual, string serieNorm)
        {
            if (TryDescomponerNumeroGuia(numeroReserva, out var serie, out var sec) && serie == serieNorm) return sec;
            var manual = SoloDigitos(numeroManual);
            if (string.IsNullOrWhiteSpace(manual)) return string.Empty;
            if (manual.Length > 9 || !long.TryParse(manual, out var secuencial) || secuencial <= 0 || secuencial > 999999999)
                throw new Exception("El secuencial de la guia no es valido.");
            return secuencial.ToString().PadLeft(9, '0');
        }

        private static void ValidarDatosSri(
            GuiaRemision guia,
            GuiaDestinatario destinatario,
            Transportista transportista,
            IReadOnlyCollection<DetalleGuiaRemision> detalles)
        {
            var serie = NormalizarSerie(guia.Serie);
            if (serie.Length != 6 || !serie.All(char.IsDigit))
                throw new Exception("La serie de la guia debe contener establecimiento y punto de emision con 3 digitos cada uno.");

            if (!guia.FechaIniTransporte.HasValue || !guia.FechaFinTransporte.HasValue ||
                guia.FechaFinTransporte.Value.Date < guia.FechaIniTransporte.Value.Date)
                throw new Exception("El rango de fechas de transporte no es valido.");

            ValidarTextoObligatorio(guia.DireccionPartida, 300, "direccion de partida");
            ValidarTextoObligatorio(transportista.RazonSocial, 300, "razon social del transportista");
            ValidarTextoObligatorio(transportista.TipoIdentificacion, 2, "tipo de identificacion del transportista");
            ValidarTextoObligatorio(transportista.NumeroIdentificacion, 13, "identificacion del transportista");
            ValidarTextoObligatorio(guia.Placa ?? transportista.Placa, 20, "placa");
            ValidarTextoObligatorio(destinatario.IdDestinatario, 20, "identificacion del destinatario");
            ValidarTextoObligatorio(destinatario.RazonSocial, 300, "razon social del destinatario");
            ValidarTextoObligatorio(destinatario.Direccion, 300, "direccion del destinatario");
            ValidarTextoObligatorio(destinatario.MotivoTraslado, 300, "motivo de traslado");

            if (!string.IsNullOrWhiteSpace(destinatario.CodEstablecimiento) &&
                (destinatario.CodEstablecimiento.Length != 3 || !destinatario.CodEstablecimiento.All(char.IsDigit)))
                throw new Exception("El codigo del establecimiento de destino debe tener 3 digitos.");
            if (!string.IsNullOrWhiteSpace(destinatario.DocAduanero) && destinatario.DocAduanero.Trim().Length > 20)
                throw new Exception("El documento aduanero no puede superar 20 caracteres.");
            if (!string.IsNullOrWhiteSpace(destinatario.Ruta) && destinatario.Ruta.Trim().Length > 300)
                throw new Exception("La ruta no puede superar 300 caracteres.");
            if (!string.IsNullOrWhiteSpace(transportista.ContribuyenteEsp) && transportista.ContribuyenteEsp.Trim().Length > 13)
                throw new Exception("El numero de contribuyente especial no puede superar 13 caracteres.");

            foreach (var detalle in detalles)
            {
                ValidarTextoObligatorio(detalle.Descripcion, 300, "descripcion del detalle");
                if ((detalle.Cantidad ?? 0) <= 0)
                    throw new Exception("Todas las cantidades de la guia deben ser mayores a cero.");
                if (!string.IsNullOrWhiteSpace(detalle.CodInterno) && detalle.CodInterno.Trim().Length > 25)
                    throw new Exception("El codigo interno de un detalle no puede superar 25 caracteres.");
                if (!string.IsNullOrWhiteSpace(detalle.CodAdicional) && detalle.CodAdicional.Trim().Length > 25)
                    throw new Exception("El codigo adicional de un detalle no puede superar 25 caracteres.");
            }
        }

        private static void ValidarEmisorSri(Emisor emisor)
        {
            var ruc = SoloDigitos(emisor.Ruc);
            if (ruc.Length != 13)
                throw new Exception("El RUC del emisor debe tener 13 digitos para emitir la guia al SRI.");
            if (string.IsNullOrWhiteSpace(emisor.RazonSocial) || emisor.RazonSocial.Trim().Length > 300)
                throw new Exception("La razon social del emisor no es valida para el SRI.");
            if (string.IsNullOrWhiteSpace(emisor.DireccionMatriz) || emisor.DireccionMatriz.Trim().Length > 300)
                throw new Exception("La direccion matriz del emisor es obligatoria para emitir la guia al SRI.");
        }

        private static string ResolverTipoIdentificacionTransportista(string? tipo, string? identificacion)
        {
            var tipoLimpio = Limpiar(tipo);
            if (tipoLimpio?.Length == 2 && tipoLimpio.All(char.IsDigit))
                return tipoLimpio;

            var identificacionLimpia = Limpiar(identificacion) ?? string.Empty;
            return identificacionLimpia.All(char.IsDigit) switch
            {
                true when identificacionLimpia.Length == 13 => "04",
                true when identificacionLimpia.Length == 10 => "05",
                _ => "06"
            };
        }

        private static void ValidarDocumentoSustentoSri(GuiaDestinatario destinatario)
        {
            var tieneSustento = !string.IsNullOrWhiteSpace(destinatario.CodDocSustento) ||
                                !string.IsNullOrWhiteSpace(destinatario.NumDocSustento) ||
                                !string.IsNullOrWhiteSpace(destinatario.SerieDocSustento);
            if (!tieneSustento)
                return;

            var numero = SoloDigitos(FormatearNumeroDocumento(destinatario.SerieDocSustento, destinatario.NumDocSustento));
            if (numero.Length != 15)
                throw new Exception("El documento de sustento debe contener establecimiento, punto de emision y secuencial.");

            var autorizacion = SoloDigitos(destinatario.NumAutorizacionSustento);
            if (!string.IsNullOrWhiteSpace(autorizacion) && autorizacion.Length is not (10 or 37 or 49))
                throw new Exception("La autorizacion del documento de sustento debe tener 10, 37 o 49 digitos.");

            if (!destinatario.FechaEmiSustento.HasValue)
                throw new Exception("La fecha de emision del documento de sustento es obligatoria.");
        }

        private static void ValidarTextoObligatorio(string? valor, int maximo, string campo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                throw new Exception($"La {campo} es obligatoria.");
            if (valor.Trim().Length > maximo)
                throw new Exception($"La {campo} no puede superar {maximo} caracteres.");
        }

        private static async Task<Emisor?> ResolverEmisorGuiaAsync(
            AppDbContext context,
            GuiaRemision guia,
            Factura? factura)
        {
            if (factura?.Codemisor is > 0)
            {
                var emisorFactura = await context.Emisores.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Codigo == factura.Codemisor.Value);
                if (emisorFactura != null)
                    return emisorFactura;
            }

            return await context.Emisores.AsNoTracking()
                .Where(e => e.Estado &&
                            e.IdEmpresa == guia.IdEmpresa &&
                            e.IdSucursal == guia.IdSucursal)
                .OrderByDescending(e => e.IdUsuario == guia.IdUsuario)
                .ThenByDescending(e => e.EsEmisorSistema)
                .FirstOrDefaultAsync();
        }

        private static Emisor? ResolverEmisorSede(GuiaRemision guia, IEnumerable<Emisor> emisores)
            => emisores
                .Where(e => e.IdEmpresa == guia.IdEmpresa && e.IdSucursal == guia.IdSucursal)
                .OrderByDescending(e => e.IdUsuario == guia.IdUsuario)
                .ThenByDescending(e => e.EsEmisorSistema)
                .FirstOrDefault();

        private static mensajeSRI CrearErrorSri(string mensaje) => new()
        {
            estado = "ERROR",
            mensaje = mensaje
        };

        private static async Task<mensajeSRI> RegistrarErrorSriAsync(
            AppDbContext context,
            GuiaRemision guia,
            string mensaje)
        {
            guia.EstadoSRI = "N";
            guia.Mensaje = mensaje;
            guia.FechaAutorizacion = DateTime.Now.ToString("O");
            await context.SaveChangesAsync();
            return CrearErrorSri(mensaje);
        }

        private static string FormatearNumeroDocumento(string? serie, string? numero)
        {
            var numeroLimpio = SoloDigitos(numero);
            if (numeroLimpio.Length == 15) return $"{numeroLimpio[..3]}-{numeroLimpio.Substring(3, 3)}-{numeroLimpio.Substring(6, 9)}";
            var serieLimpia = NormalizarSerie(serie);
            if (serieLimpia.Length == 6 && !string.IsNullOrWhiteSpace(numeroLimpio))
                return $"{serieLimpia[..3]}-{serieLimpia.Substring(3, 3)}-{numeroLimpio.PadLeft(9, '0')}";
            return Limpiar(numero) ?? string.Empty;
        }

        private static string FormatearNumeroCompleto(string? serie, string? numero)
            => $"{FormatearSerie(serie)}-{SoloDigitos(numero).PadLeft(9, '0')}";

        private static string SoloDigitos(string? valor) => new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        private static string NormalizarSerie(string? serie) => string.IsNullOrWhiteSpace(serie) ? string.Empty : serie.Replace("-", string.Empty).Trim();
        private static string FormatearSerie(string? serie) => NormalizarSerie(serie) is var s && s.Length == 6 ? $"{s[..3]}-{s[3..]}" : NormalizarSerie(serie);
        private static string? NormalizarSiNo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : (new[] { "S", "SI", "1", "TRUE", "T" }.Contains(valor.Trim().ToUpperInvariant()) ? "SI" : "NO");

        private static bool TryDescomponerNumeroGuia(string? numeroCompleto, out string serieNorm, out string secuencial)
        {
            serieNorm = string.Empty; secuencial = string.Empty;
            var limpio = SoloDigitos(numeroCompleto);
            if (limpio.Length != 15) return false;
            serieNorm = limpio[..6];
            secuencial = limpio.Substring(6, 9);
            return true;
        }
    }
}
