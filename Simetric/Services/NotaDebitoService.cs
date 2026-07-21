using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System.Data;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Simetric.Services;

public class NotaDebitoService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly INotaDebitoPdfService _notaDebitoPdfService;
    private readonly ICajaSerieResolver _cajaSerieResolver;
    private readonly EmisionControlService _emisionControlService;
    private readonly IEmailService _emailService;
    private readonly ComprobanteCorreoEstadoService _comprobanteCorreoEstadoService;
    private readonly InitialSequencePromptService _initialSequencePromptService;
    private readonly SriXmlProcessorService _sriXmlProcessorService;

    public NotaDebitoService(
        IDbContextFactory<AppDbContext> dbFactory,
        IWebHostEnvironment env,
        INotaDebitoPdfService notaDebitoPdfService,
        ICajaSerieResolver cajaSerieResolver,
        EmisionControlService emisionControlService,
        IEmailService emailService,
        ComprobanteCorreoEstadoService comprobanteCorreoEstadoService,
        InitialSequencePromptService initialSequencePromptService,
        SriXmlProcessorService sriXmlProcessorService)
    {
        _dbFactory = dbFactory;
        _env = env;
        _notaDebitoPdfService = notaDebitoPdfService;
        _cajaSerieResolver = cajaSerieResolver;
        _emisionControlService = emisionControlService;
        _emailService = emailService;
        _comprobanteCorreoEstadoService = comprobanteCorreoEstadoService;
        _initialSequencePromptService = initialSequencePromptService;
        _sriXmlProcessorService = sriXmlProcessorService;
    }

    private async Task<CajaSerieResolucion> ResolverSerieNotaDebitoAsync(int userId, string? serieRaw = null)
    {
        if (!string.IsNullOrWhiteSpace(serieRaw))
            return await _cajaSerieResolver.ResolverAsync(userId, serieRaw);

        var resolucionBase = await _cajaSerieResolver.ResolverAsync(userId);
        var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
            userId,
            "nota-debito",
            resolucionBase.SerieRaw);

        if (!string.IsNullOrWhiteSpace(seriePreferida) &&
            !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
        {
            return await _cajaSerieResolver.ResolverAsync(userId, seriePreferida);
        }

        return resolucionBase;
    }

    private static async Task<List<int>> ObtenerUsuariosCuentaIdsAsync(AppDbContext db, int idUsuario)
    {
        var usuario = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
            .FirstOrDefaultAsync();

        if (usuario == null)
            return new List<int> { idUsuario };

        var titularId = usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : usuario.IdUsuario;

        var usuariosCuenta = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();

        if (usuariosCuenta.Count == 0)
            usuariosCuenta.Add(idUsuario);

        return usuariosCuenta;
    }

    public class DetalleNdDto
    {
        public int Codproducto { get; set; }
        public string? CodPrincipal { get; set; }
        public string? CodAuxiliar { get; set; }
        public decimal Cantidad { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string? Detalle { get; set; }
        public decimal Preciounitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal Subtotal { get; set; }
        public int Iva { get; set; }
        public decimal ValorIce { get; set; }
        public decimal Total { get; set; }
        public int CodigoImp { get; set; } = 2;
        public string? CodigoPorcentajeSri { get; set; }
        public decimal Costo { get; set; }
    }

    private sealed class ImpuestoNotaDebitoDto
    {
        public string Codigo { get; init; } = "2";
        public string CodigoPorcentaje { get; init; } = "0";
        public decimal Tarifa { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Valor { get; set; }
    }

    public async Task<List<FacturaBusquedaDto>> BuscarFacturasAutocompleteAsync(string texto, int idUsuario)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        texto = (texto ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return new List<FacturaBusquedaDto>();

        var candidatos = await (
            from f in db.Facturas.AsNoTracking()
            join c in db.Clientes.AsNoTracking() on f.Codclientes equals c.Codcliente into cliJoin
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

        return await FiltrarFacturasConSaldoDisponibleAsync(db, candidatos);
    }

    public async Task<List<DetalleNdDto>> ObtenerDetallesFacturaAsync(int codFactura)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.Detallefacturas
            .AsNoTracking()
            .Where(d => d.Codfactura == codFactura)
            .OrderBy(d => d.Codlinea)
            .Select(d => new DetalleNdDto
            {
                Codproducto = d.Codproducto,
                CodPrincipal = d.Codprincipal,
                CodAuxiliar = d.Codauxiliar,
                Cantidad = d.Cantproducto,
                Descripcion = d.Descripproducto ?? string.Empty,
                Detalle = d.Descripproducto ?? string.Empty,
                Preciounitario = d.Precioproducto,
                Descuento = d.Descuento ?? 0m,
                Subtotal = d.Valortproducto,
                Iva = d.Tarifa,
                Total = d.Valortotal,
                CodigoImp = 2,
                CodigoPorcentajeSri = ObtenerCodigoPorcentajeSri(d.Tarifa),
                Costo = d.Costo ?? 0m
            })
            .ToListAsync();
    }

    public async Task<int> ResolverClienteParaNotaDebitoAsync(int idUsuario, Cliente clienteEntrada)
    {
        if (idUsuario <= 0)
            throw new InvalidOperationException("No se pudo identificar el usuario para asociar el cliente.");

        var identificacion = LimpiarTextoNotaDebito(clienteEntrada.Numeroidentificacion);
        var nombre = LimpiarTextoNotaDebito(clienteEntrada.Nombrerazonsocial)
            ?? LimpiarTextoNotaDebito($"{clienteEntrada.Nombres} {clienteEntrada.Apellidos}");

        if (string.IsNullOrWhiteSpace(identificacion))
            throw new InvalidOperationException("Ingresa la identificacion del cliente.");

        if (string.IsNullOrWhiteSpace(nombre))
            throw new InvalidOperationException("Ingresa el nombre o razon social del cliente.");

        await using var db = await _dbFactory.CreateDbContextAsync();

        var ownerId = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => u.idJefe ?? u.IdUsuario)
            .FirstOrDefaultAsync();

        if (ownerId <= 0)
            ownerId = idUsuario;

        var cliente = await db.Clientes
            .FirstOrDefaultAsync(c => c.Usuario == ownerId && c.Numeroidentificacion == identificacion);

        if (cliente is not null)
        {
            var tipoIdentificacion = LimpiarTextoNotaDebito(clienteEntrada.Tipoidentificacion);
            var direccion = LimpiarTextoNotaDebito(clienteEntrada.Direccion);
            var celular = LimpiarTextoNotaDebito(clienteEntrada.Celular);
            var correo = LimpiarTextoNotaDebito(clienteEntrada.Correo);

            cliente.Nombrerazonsocial = nombre;
            cliente.Tipoidentificacion = tipoIdentificacion ?? cliente.Tipoidentificacion ?? "05";
            cliente.Direccion = direccion ?? cliente.Direccion;
            cliente.Celular = celular ?? cliente.Celular;
            cliente.Correo = correo ?? cliente.Correo;
            cliente.TipoCliente = clienteEntrada.TipoCliente ?? cliente.TipoCliente;
            cliente.Estado = true;

            await db.SaveChangesAsync();
            return cliente.Codcliente;
        }

        cliente = new Cliente
        {
            Usuario = ownerId,
            Estado = true,
            Tipoidentificacion = LimpiarTextoNotaDebito(clienteEntrada.Tipoidentificacion) ?? "05",
            Numeroidentificacion = identificacion,
            Nombrerazonsocial = nombre,
            Direccion = LimpiarTextoNotaDebito(clienteEntrada.Direccion),
            Celular = LimpiarTextoNotaDebito(clienteEntrada.Celular),
            Correo = LimpiarTextoNotaDebito(clienteEntrada.Correo),
            TipoCliente = clienteEntrada.TipoCliente
        };

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Codcliente;
    }

    private static string? LimpiarTextoNotaDebito(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    public async Task<int> CrearAsync(
        NotaDebito notaDebito,
        List<DetalleNdDto> detalles,
        List<FacturaCorreoDestinoDto>? correosNota = null)
    {
        if (notaDebito == null)
            throw new ArgumentNullException(nameof(notaDebito));

        if (detalles == null || detalles.Count == 0)
            throw new InvalidOperationException("La nota de debito debe contener al menos un detalle.");

        if (notaDebito.Usuario is not > 0)
            throw new Exception("No se pudo identificar el usuario para asignar la serie de la nota de débito.");

        await _emisionControlService.AsegurarPuedeEmitirAsync(notaDebito.Usuario.Value);

        var resolucion = await ResolverSerieNotaDebitoAsync(notaDebito.Usuario.Value, notaDebito.Serie);
        notaDebito.Serie = resolucion.SerieRaw;
        notaDebito.Ambiente = 2;
        NormalizarYValidarNotaDebito(notaDebito, detalles);
        RecalcularTotales(notaDebito, detalles);
        var correosGuardarEnCliente = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(
            correosNota?
                .Where(x => x.GuardarEnCliente)
                .Select(x => x.Correo));

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var emisor = await db.Emisores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Codigo == notaDebito.CodEmisor);

                ValidarEmisorSri(emisor);
                notaDebito.Ambiente = 2;

                var serie = LimpiarSerie(notaDebito.Serie);
                var secuencial = LimpiarSecuencial(notaDebito.NumNotaDebito);
                var usuariosCuenta = await ObtenerUsuariosCuentaIdsAsync(db, notaDebito.Usuario.Value);
                var duplicada = await db.NotaDebitos.AsNoTracking().AnyAsync(n =>
                    n.Usuario.HasValue && usuariosCuenta.Contains(n.Usuario.Value) &&
                    n.CodEmisor == notaDebito.CodEmisor &&
                    n.Serie != null && n.Serie.Replace("-", string.Empty) == serie &&
                    n.NumNotaDebito == secuencial);
                if (duplicada)
                    throw new InvalidOperationException($"La nota de debito {FormatearNumeroCompleto(serie, secuencial)} ya existe.");

                if (notaDebito.IdDocModificado is > 0)
                {
                    var facturaOriginal = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(f =>
                        f.Codfactura == notaDebito.IdDocModificado.Value &&
                        f.Idusuario == notaDebito.Usuario.Value);
                    if (facturaOriginal == null)
                        throw new InvalidOperationException("La factura modificada no existe o no pertenece al usuario actual.");
                    if (!DocumentoAutorizacionHelper.EstaAutorizado(facturaOriginal.Autorizado, facturaOriginal.Estadoenviosri))
                        throw new InvalidOperationException("La factura modificada debe estar autorizada por el SRI.");
                }

                AsegurarClaveAcceso(notaDebito, emisor);
                notaDebito.Autorizado = "0";
                notaDebito.Mensaje = DocumentoAutorizacionHelper.EstadoPendiente;

                db.NotaDebitos.Add(notaDebito);
                await db.SaveChangesAsync();

                foreach (var d in detalles)
                {
                    var subtotalLinea = Red2(Math.Max(0m, d.Subtotal));
                    var ivaLinea = Red2(Math.Max(0m, subtotalLinea * d.Iva / 100m));

                    db.DetallesNotaDebito.Add(new DetalleNotaDebito
                    {
                        CodNotaDebito = notaDebito.Sec,
                        CodProducto = d.Codproducto,
                        CodPrincipal = d.CodPrincipal,
                        CodAuxiliar = d.CodAuxiliar,
                        CantProducto = d.Cantidad,
                        DescripProducto = d.Descripcion,
                        PrecioProducto = d.Preciounitario,
                        Descuento = d.Descuento,
                        ValorTProducto = subtotalLinea,
                        ValorIce = d.ValorIce,
                        ValorIva = ivaLinea,
                        BiIrbpnr = 0m,
                        ValorBiIrbpnr = 0m,
                        PorcentajeIva = d.Iva,
                        CodigoImp = d.CodigoImp,
                        Costo = d.Costo
                    });
                }

                await db.SaveChangesAsync();

                if (notaDebito.CodClientes is > 0 && correosGuardarEnCliente.Any())
                {
                    var cliente = await db.Clientes
                        .FirstOrDefaultAsync(c => c.Codcliente == notaDebito.CodClientes.Value);

                    if (cliente != null)
                    {
                        var correosExistentes = await db.ClientesCorreos
                            .Where(cc => cc.CodCliente == cliente.Codcliente && cc.Estado)
                            .Select(cc => cc.Correo)
                            .ToListAsync();

                        var hashCorreos = new HashSet<string>(
                            correosExistentes
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x!.Trim()),
                            StringComparer.OrdinalIgnoreCase);

                        foreach (var correo in correosGuardarEnCliente)
                        {
                            if (string.Equals(correo, cliente.Correo?.Trim(), StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (hashCorreos.Contains(correo))
                                continue;

                            db.ClientesCorreos.Add(new ClienteCorreo
                            {
                                CodCliente = cliente.Codcliente,
                                Correo = correo,
                                Estado = true
                            });

                            hashCorreos.Add(correo);
                        }

                        await db.SaveChangesAsync();
                    }
                }

                await _emisionControlService.ConsumirDocumentoAsync(db, notaDebito.Usuario.Value);
                await tx.CommitAsync();

                return notaDebito.Sec;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<List<NotaDebitoListDto>> ListarNotasDebitoUsuarioAsync(int idUsuario)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var data = await (
            from nd in db.NotaDebitos.AsNoTracking()
            join c in db.Clientes.AsNoTracking()
                on nd.CodClientes equals c.Codcliente into cliJoin
            from c in cliJoin.DefaultIfEmpty()

            join e in db.Emisores.AsNoTracking()
                on nd.CodEmisor equals e.Codigo into emiJoin
            from e in emiJoin.DefaultIfEmpty()

            join f in db.Facturas.AsNoTracking()
                on nd.IdDocModificado equals f.Codfactura into facJoin
            from f in facJoin.DefaultIfEmpty()

            join ti in db.Identificacion.AsNoTracking()
                on c.Tipoidentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()

            where nd.Usuario == idUsuario &&
                  (e == null || e.EsEmisorSistema != true) &&
                  (nd.Estado == null || nd.Estado != "I")
            orderby nd.Sec descending
            select new
            {
                Sec = nd.Sec,
                NumeroNotaDebito = nd.NumNotaDebito ?? string.Empty,
                Serie = nd.Serie ?? string.Empty,
                Cliente = c != null
                    ? (!string.IsNullOrWhiteSpace(c.Nombrerazonsocial)
                        ? c.Nombrerazonsocial
                        : ((c.Nombres ?? string.Empty) + " " + (c.Apellidos ?? string.Empty)).Trim())
                    : string.Empty,
                IdentificacionCliente = c != null ? (c.Numeroidentificacion ?? string.Empty) : string.Empty,
                TipoIdentificacionCliente = ti != null ? (ti.IdeDescripcion ?? string.Empty) : string.Empty,
                NumeroDocModificado = nd.NumDocModificado ?? string.Empty,
                SerieDocModificado = f != null ? (f.Serie ?? string.Empty) : string.Empty,
                FechaDocumentoModificado = nd.FechaEmiDocModificado,
                Subtotal = nd.Subtotal ?? 0m,
                Iva = nd.Iva ?? 0m,
                Total = nd.ValorTotal ?? 0m,
                Motivo = nd.Motivo ?? string.Empty,
                Estado = nd.Estado,
                Autorizado = nd.Autorizado ?? string.Empty,
                NumeroAutorizacion = nd.NumAutorizacion ?? string.Empty,
                MensajeSri = nd.Mensaje ?? string.Empty,
                FechaAutorizacion = nd.FchAutorizacion,
                RucEmisor = e != null ? (e.Ruc ?? string.Empty) : string.Empty
            })
            .ToListAsync();

        return data.Select(x => new NotaDebitoListDto
        {
            Sec = x.Sec,
            NumeroNotaDebito = x.NumeroNotaDebito,
            Serie = x.Serie,
            Cliente = x.Cliente,
            IdentificacionCliente = x.IdentificacionCliente,
            TipoIdentificacionCliente = x.TipoIdentificacionCliente,
            NumeroDocModificado = x.NumeroDocModificado,
            NumeroDocModificadoVisual = FormatearDocModificado(x.SerieDocModificado, x.NumeroDocModificado),
            FechaDocumentoModificado = x.FechaDocumentoModificado,
            Subtotal = x.Subtotal,
            Iva = x.Iva,
            Total = x.Total,
            Motivo = x.Motivo,
            Estado = EstadoActivo(x.Estado),
            Autorizado = x.Autorizado,
            NumeroAutorizacion = x.NumeroAutorizacion,
            MensajeSri = x.MensajeSri,
            FechaAutorizacion = x.FechaAutorizacion,
            XmlUrl = !string.IsNullOrWhiteSpace(x.RucEmisor)
                ? ConstruirXmlUrl(x.NumeroNotaDebito, x.RucEmisor)
                : string.Empty
        }).ToList();
    }

    public async Task<mensajeSRI> EmitirNotaDebitoSriAsync(
        int sec,
        int? idUsuario = null,
        bool intentarEnviarCorreo = true,
        IEnumerable<string?>? correosExtra = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var nota = await db.NotaDebitos.FirstOrDefaultAsync(n => n.Sec == sec);
        if (nota == null)
            return CrearErrorSri("No se encontro la nota de debito para enviar al SRI.");
        if (idUsuario.HasValue && nota.Usuario != idUsuario.Value)
            return CrearErrorSri("La nota de debito no pertenece al usuario actual.");
        if (!EstadoActivo(nota.Estado))
            return CrearErrorSri("La nota de debito esta anulada y ya no puede reenviarse al SRI.");

        if (NotaDebitoEstaAutorizada(nota.Autorizado))
        {
            if (intentarEnviarCorreo)
                await IntentarEnviarNotaDebitoPorCorreoAsync(sec, correosExtra: correosExtra);

            return new mensajeSRI
            {
                estado = DocumentoAutorizacionHelper.EstadoAutorizado,
                autorizacion = nota.NumAutorizacion ?? string.Empty,
                fecha = nota.FechaAutoSri ?? nota.FchAutorizacion?.ToString("O") ?? string.Empty,
                mensaje = "La nota de debito ya se encuentra autorizada."
            };
        }

        var detalle = await GetNotaDebitoDetalleAsync(sec);
        if (detalle?.Emisor == null)
            return await RegistrarErrorSriAsync(db, nota, "No se encontro el emisor asociado a la nota de debito.");

        var emisor = detalle.Emisor;
        if (string.IsNullOrWhiteSpace(emisor.PathCertificado) || string.IsNullOrWhiteSpace(emisor.ClaveCertificado))
            return await RegistrarErrorSriAsync(db, nota, "El emisor no tiene configurado el certificado electronico requerido para enviar la nota de debito al SRI.");

        var rutaXml = await ProcesarXmlNotaDebitoAsync(sec);
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            return await RegistrarErrorSriAsync(db, nota, "No se pudo generar el XML de la nota de debito para enviarlo al SRI.");

        var respuesta = await _sriXmlProcessorService.ProcessXmlAsync(
            rutaXml,
            emisor.PathCertificado,
            emisor.ClaveCertificado);
        var autorizada = string.Equals(
            respuesta.estado,
            DocumentoAutorizacionHelper.EstadoAutorizado,
            StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(respuesta.autorizacion);

        nota.Autorizado = autorizada ? "1" : "0";
        nota.NumAutorizacion = string.IsNullOrWhiteSpace(respuesta.autorizacion)
            ? nota.NumAutorizacion
            : respuesta.autorizacion.Trim();
        nota.FechaAutoSri = string.IsNullOrWhiteSpace(respuesta.fecha)
            ? DateTime.Now.ToString("O")
            : respuesta.fecha;
        nota.FchAutorizacion = autorizada ? DateTime.Now : nota.FchAutorizacion;
        nota.Mensaje = autorizada
            ? "ok"
            : string.IsNullOrWhiteSpace(respuesta.mensaje) ? respuesta.estado : respuesta.mensaje;
        await db.SaveChangesAsync();

        if (autorizada)
        {
            if (nota.Usuario is > 0)
                await AsegurarPdfNotaDebitoUsuarioAsync(sec, nota.Usuario.Value);
            if (intentarEnviarCorreo)
                await IntentarEnviarNotaDebitoPorCorreoAsync(sec, rutaXml, correosExtra: correosExtra);
        }

        return respuesta;
    }

    public async Task<NotaDebitoDetalleViewDto?> GetNotaDebitoDetalleUsuarioAsync(int sec, int idUsuario)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existe = await db.NotaDebitos
            .AsNoTracking()
            .AnyAsync(x => x.Sec == sec && x.Usuario == idUsuario);

        if (!existe)
            return null;

        return await GetNotaDebitoDetalleAsync(sec);
    }

    public async Task<string?> AsegurarXmlNotaDebitoUsuarioAsync(int sec, int idUsuario)
    {
        var detalle = await GetNotaDebitoDetalleUsuarioAsync(sec, idUsuario);
        if (detalle?.NotaDebito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaXml = ConstruirXmlPath(detalle.NotaDebito.NumNotaDebito ?? string.Empty, detalle.Emisor.Ruc ?? string.Empty);
        if (!File.Exists(rutaXml) || await XmlNotaDebitoNecesitaRegeneracionAsync(rutaXml))
            rutaXml = await ProcesarXmlNotaDebitoAsync(sec);

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            return null;

        return ConstruirXmlUrl(detalle.NotaDebito.NumNotaDebito ?? string.Empty, detalle.Emisor.Ruc ?? string.Empty);
    }

    public async Task<string?> AsegurarPdfNotaDebitoUsuarioAsync(int sec, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var detalle = await GetNotaDebitoDetalleUsuarioAsync(sec, idUsuario);
        if (detalle?.NotaDebito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaPdf = await _notaDebitoPdfService.GenerarPdfNotaDebitoAsync(detalle, formato);

        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
            return null;

        return ConstruirPdfUrl(detalle.NotaDebito.NumNotaDebito ?? string.Empty, detalle.Emisor.Ruc ?? string.Empty, formato);
    }

    public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarNotaDebitoPorCorreoAsync(
        int sec,
        string? rutaXmlExistente = null,
        string? rutaPdfExistente = null,
        IEnumerable<string?>? correosExtra = null,
        bool forzarReenvio = false)
    {
        var detalle = await GetNotaDebitoDetalleAsync(sec);
        if (detalle?.NotaDebito == null)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "No se encontró la nota de débito para enviar por correo."
            };
        }

        if (!EstadoActivo(detalle.NotaDebito.Estado))
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "La nota de dÃ©bito estÃ¡ anulada y ya no puede reenviarse por correo."
            };
        }

        var seguimiento = await _comprobanteCorreoEstadoService.GetEstadoAsync(
            ComprobanteCorreoEstadoService.TipoNotaDebito,
            sec);
        if (!forzarReenvio && seguimiento?.CorreoEnviado == true)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                YaEnviado = true,
                Mensaje = "El correo de esta nota de débito ya fue enviado anteriormente.",
                TotalDestinatarios = 0
            };
        }

        await using var context = await _dbFactory.CreateDbContextAsync();

        var destinatarios = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            context,
            detalle.NotaDebito.Usuario,
            detalle.NotaDebito.CodClientes,
            detalle.Cliente?.Correo,
            correosExtra);

        if (!destinatarios.Any())
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                SinDestinatarios = true,
                Mensaje = "La nota de débito no tiene correos configurados para el envío."
            };
        }

        if (!NotaDebitoEstaAutorizada(detalle.NotaDebito.Autorizado))
        {
            await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec);

            return new FacturaCorreoEnvioResultadoDto
            {
                PendienteAutorizacion = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La nota de débito aún no está autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s) hasta que Autorizado tenga un valor aprobado."
            };
        }

        if (detalle.Emisor == null || string.IsNullOrWhiteSpace(detalle.Emisor.Ruc))
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec,
                "No se pudo identificar el emisor de la nota de débito para enviar el correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = "No se pudo identificar el emisor de la nota de débito para enviar el correo."
            };
        }

        var rutaXml = rutaXmlExistente;
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            rutaXml = ConstruirXmlPath(detalle.NotaDebito.NumNotaDebito ?? string.Empty, detalle.Emisor.Ruc);
            if (!File.Exists(rutaXml) || await XmlNotaDebitoNecesitaRegeneracionAsync(rutaXml))
                rutaXml = await ProcesarXmlNotaDebitoAsync(sec);
        }

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec,
                "No se pudo generar o ubicar el XML adjunto para enviar la nota de débito por correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = "No se pudo generar o ubicar el XML adjunto para enviar la nota de débito por correo."
            };
        }

        var rutaPdf = rutaPdfExistente;
        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
        {
            rutaPdf = ConstruirPdfPath(detalle.NotaDebito.NumNotaDebito ?? string.Empty, detalle.Emisor.Ruc);
            if (!File.Exists(rutaPdf))
                rutaPdf = await _notaDebitoPdfService.GenerarPdfNotaDebitoAsync(detalle);
        }

        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec,
                "No se pudo generar o ubicar el PDF adjunto para enviar la nota de débito por correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = "No se pudo generar o ubicar el PDF adjunto para enviar la nota de débito por correo."
            };
        }

        try
        {
            await _emailService.EnviarNotaDebitoAsync(
                detalle.NumeroCompleto,
                detalle.NumeroDocModificadoVisual,
                destinatarios,
                ObtenerNombreCliente(detalle.Cliente),
                detalle.NotaDebito.ValorTotal,
                rutaXml,
                rutaPdf);

            await _comprobanteCorreoEstadoService.MarcarEnviadoAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec);

            return new FacturaCorreoEnvioResultadoDto
            {
                Enviado = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"Se envió el correo con XML y PDF de la nota de débito a {destinatarios.Count} destinatario(s)."
            };
        }
        catch (Exception ex)
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoNotaDebito,
                sec,
                ex.Message);

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"No se pudo enviar el correo de la nota de débito: {ex.Message}"
            };
        }
    }

    public async Task<List<int>> GetNotasDebitoAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
    {
        var idsPendientes = await _comprobanteCorreoEstadoService.GetDocumentosPendientesAsync(
            ComprobanteCorreoEstadoService.TipoNotaDebito,
            Math.Max(maxRegistros * 5, maxRegistros));

        if (!idsPendientes.Any())
            return new List<int>();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var candidatos = await db.NotaDebitos
            .AsNoTracking()
            .Where(n => idsPendientes.Contains(n.Sec))
            .Select(n => new { n.Sec, n.Autorizado, n.Estado })
            .ToListAsync();

        return candidatos
            .Where(n => EstadoActivo(n.Estado) && NotaDebitoEstaAutorizada(n.Autorizado))
            .OrderBy(n => n.Sec)
            .Take(maxRegistros)
            .Select(n => n.Sec)
            .ToList();
    }

    public async Task<bool> AnularNotaDebitoDirectoAsync(int sec, int? idUsuario = null)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        try
        {
            var nota = await context.NotaDebitos.FirstOrDefaultAsync(n =>
                n.Sec == sec &&
                (!idUsuario.HasValue || n.Usuario == idUsuario.Value));

            if (nota == null)
                return false;

            nota.Estado = "I";
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<NotaDebitoDetalleViewDto?> GetNotaDebitoDetalleAsync(int sec)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var rawData = await (
            from nd in db.NotaDebitos.AsNoTracking()
            join c in db.Clientes.AsNoTracking()
                on nd.CodClientes equals c.Codcliente into cliJoin
            from c in cliJoin.DefaultIfEmpty()

            join e in db.Emisores.AsNoTracking()
                on nd.CodEmisor equals e.Codigo into emiJoin
            from e in emiJoin.DefaultIfEmpty()

            join ti in db.Identificacion.AsNoTracking()
                on c.Tipoidentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()

            join f in db.Facturas.AsNoTracking()
                on nd.IdDocModificado equals f.Codfactura into facJoin
            from f in facJoin.DefaultIfEmpty()

            where nd.Sec == sec
            select new
            {
                NotaDebito = nd,
                Cliente = c,
                Emisor = e,
                TipoIdentificacionCliente = ti != null ? (ti.IdeDescripcion ?? string.Empty) : string.Empty,
                SerieDocModificado = f != null ? (f.Serie ?? string.Empty) : string.Empty,
                FormaPagoCodigo = f != null ? (f.Tipopago ?? string.Empty) : string.Empty,
                DiasPlazo = f != null ? f.Tiempocredito : null
            })
            .FirstOrDefaultAsync();

        if (rawData == null)
            return null;

        var formaPagoNombre = string.Empty;
        if (!string.IsNullOrWhiteSpace(rawData.FormaPagoCodigo))
        {
            formaPagoNombre = await db.FormasPago
                .AsNoTracking()
                .Where(x => x.Codigo == rawData.FormaPagoCodigo)
                .Select(x => x.Descripcion ?? x.DescripcionSri ?? string.Empty)
                .FirstOrDefaultAsync();
        }

        var detalles = await db.DetallesNotaDebito
            .AsNoTracking()
            .Where(d => d.CodNotaDebito == sec)
            .OrderBy(d => d.CodLinea)
            .Select(d => new NotaDebitoDetalleLineaDto
            {
                CodigoInterno = !string.IsNullOrWhiteSpace(d.CodPrincipal)
                    ? d.CodPrincipal
                    : d.CodProducto.ToString(),
                Descripcion = d.DescripProducto ?? string.Empty,
                Cantidad = d.CantProducto ?? 0m,
                PrecioUnitario = d.PrecioProducto ?? 0m,
                Descuento = d.Descuento ?? 0m,
                Subtotal = d.ValorTProducto ?? 0m,
                TarifaIva = d.PorcentajeIva ?? 0m,
                ValorIce = d.ValorIce ?? 0m,
                ValorIva = d.ValorIva ?? 0m,
                Total = (d.ValorTProducto ?? 0m) + (d.ValorIva ?? 0m) + (d.ValorIce ?? 0m)
            })
            .ToListAsync();

        return new NotaDebitoDetalleViewDto
        {
            NotaDebito = rawData.NotaDebito,
            Cliente = rawData.Cliente,
            Emisor = rawData.Emisor,
            TipoIdentificacionCliente = rawData.TipoIdentificacionCliente,
            NumeroCompleto = FormatearNumeroCompleto(rawData.NotaDebito.Serie, rawData.NotaDebito.NumNotaDebito),
            NumeroDocModificadoVisual = FormatearDocModificado(rawData.SerieDocModificado, rawData.NotaDebito.NumDocModificado),
            XmlUrl = !string.IsNullOrWhiteSpace(rawData.Emisor?.Ruc)
                ? ConstruirXmlUrl(rawData.NotaDebito.NumNotaDebito ?? string.Empty, rawData.Emisor.Ruc ?? string.Empty)
                : string.Empty,
            FormaPago = rawData.FormaPagoCodigo,
            FormaPagoNombre = formaPagoNombre ?? string.Empty,
            DiasPlazo = rawData.DiasPlazo,
            Detalles = detalles
        };
    }

    public async Task<string> ProcesarXmlNotaDebitoAsync(int sec)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var notaDebito = await db.NotaDebitos.FirstOrDefaultAsync(x => x.Sec == sec);
        if (notaDebito == null)
            throw new InvalidOperationException("No se encontro la nota de debito para generar el XML.");

        var detalles = await db.DetallesNotaDebito
            .AsNoTracking()
            .Where(d => d.CodNotaDebito == sec)
            .OrderBy(d => d.CodLinea)
            .Select(d => new DetalleNdDto
            {
                Codproducto = d.CodProducto,
                CodPrincipal = d.CodPrincipal,
                CodAuxiliar = d.CodAuxiliar,
                Cantidad = d.CantProducto ?? 0m,
                Descripcion = d.DescripProducto ?? string.Empty,
                Detalle = d.DescripProducto ?? string.Empty,
                Preciounitario = d.PrecioProducto ?? 0m,
                Descuento = d.Descuento ?? 0m,
                Subtotal = d.ValorTProducto ?? 0m,
                Iva = (int)Math.Round(d.PorcentajeIva ?? 0m, MidpointRounding.AwayFromZero),
                ValorIce = d.ValorIce ?? 0m,
                Total = (d.ValorTProducto ?? 0m) + (d.ValorIva ?? 0m) + (d.ValorIce ?? 0m),
                CodigoImp = d.CodigoImp ?? 2,
                CodigoPorcentajeSri = ObtenerCodigoPorcentajeSri((int)Math.Round(d.PorcentajeIva ?? 0m, MidpointRounding.AwayFromZero)),
                Costo = d.Costo ?? 0m
            })
            .ToListAsync();

        var emisor = await db.Emisores.AsNoTracking().FirstOrDefaultAsync(e => e.Codigo == notaDebito.CodEmisor);
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Codcliente == notaDebito.CodClientes);
        var facturaOriginal = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(f => f.Codfactura == notaDebito.IdDocModificado);

        ValidarEmisorSri(emisor);
        if (AsegurarClaveAcceso(notaDebito, emisor))
            await db.SaveChangesAsync();

        var xmlContent = GenerarXmlNotaDebito(notaDebito, detalles, emisor, cliente, facturaOriginal);
        await GuardarXmlEnServidor(xmlContent, notaDebito.NumNotaDebito ?? string.Empty, emisor?.Ruc ?? string.Empty);

        return ConstruirXmlPath(notaDebito.NumNotaDebito ?? string.Empty, emisor?.Ruc ?? string.Empty);
    }

    public string GenerarXmlNotaDebito(
        NotaDebito notaDebito,
        List<DetalleNdDto> detalles,
        Emisor? emisor,
        Cliente? cliente,
        Factura? facturaOriginal)
    {
        var cultura = CultureInfo.InvariantCulture;
        var fechaEmision = ObtenerFechaEmision(notaDebito);
        const string ambiente = "2";
        var serieLimpia = LimpiarSerie(notaDebito.Serie);
        var secuencial = (notaDebito.NumNotaDebito ?? "1").Trim().PadLeft(9, '0');
        var formaPago = NormalizarFormaPagoSri(facturaOriginal?.Tipopago);
        var plazo = facturaOriginal?.Tiempocredito;
        var tipoEmision = NormalizarTipoEmisionSri(emisor?.TipoEmision);
        var gruposImpuesto = ConstruirGruposImpuesto(notaDebito, detalles);
        var identificacionComprador = LimpiarTextoXml(cliente?.Numeroidentificacion, string.Empty, 20);
        if (string.IsNullOrWhiteSpace(identificacionComprador))
            throw new InvalidOperationException("La identificacion del comprador es obligatoria para generar el XML.");

        var tipoIdentificacionComprador = NormalizarTipoIdentificacionSri(
            cliente?.Tipoidentificacion,
            identificacionComprador);
        var contribuyenteEspecial = NormalizarContribuyenteEspecial(emisor?.ContribuyenteEspecial);

        var claveAcceso = !string.IsNullOrWhiteSpace(notaDebito.CodClave)
            ? notaDebito.CodClave!
            : GenerarClaveAcceso(
                fechaEmision,
                emisor?.Ruc,
                ambiente,
                serieLimpia,
                secuencial,
                "05",
                tipoEmision);

        var infoAdicional = new List<XElement>();
        if (!string.IsNullOrWhiteSpace(cliente?.Direccion))
            infoAdicional.Add(new XElement("campoAdicional", new XAttribute("nombre", "Direccion"), LimpiarTextoXml(cliente.Direccion, string.Empty, 300)));
        if (!string.IsNullOrWhiteSpace(cliente?.Correo))
            infoAdicional.Add(new XElement("campoAdicional", new XAttribute("nombre", "Email"), LimpiarTextoXml(cliente.Correo, string.Empty, 300)));
        if (!string.IsNullOrWhiteSpace(cliente?.Celular))
            infoAdicional.Add(new XElement("campoAdicional", new XAttribute("nombre", "Telefono"), LimpiarTextoXml(cliente.Celular, string.Empty, 300)));

        var xml = new XElement("notaDebito",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.0.0"),

            new XElement("infoTributaria",
                new XElement("ambiente", ambiente),
                new XElement("tipoEmision", tipoEmision),
                new XElement("razonSocial", LimpiarTextoXml(emisor?.RazonSocial, string.Empty, 300)),
                !string.IsNullOrWhiteSpace(emisor?.NomComercial)
                    ? new XElement("nombreComercial", LimpiarTextoXml(emisor.NomComercial, string.Empty, 300))
                    : null,
                new XElement("ruc", SoloDigitos(emisor?.Ruc)),
                new XElement("claveAcceso", claveAcceso),
                new XElement("codDoc", "05"),
                new XElement("estab", ObtenerEstablecimiento(serieLimpia, emisor)),
                new XElement("ptoEmi", ObtenerPuntoEmision(serieLimpia, emisor)),
                new XElement("secuencial", secuencial),
                new XElement("dirMatriz", LimpiarTextoXml(emisor?.DireccionMatriz ?? emisor?.Direccion, string.Empty, 300))
            ),

            new XElement("infoNotaDebito",
                new XElement("fechaEmision", fechaEmision.ToString("dd/MM/yyyy", cultura)),
                !string.IsNullOrWhiteSpace(emisor?.DirEstablecimiento ?? emisor?.DireccionMatriz)
                    ? new XElement("dirEstablecimiento", LimpiarTextoXml(emisor?.DirEstablecimiento ?? emisor?.DireccionMatriz, string.Empty, 300))
                    : null,
                new XElement("tipoIdentificacionComprador", tipoIdentificacionComprador),
                new XElement("razonSocialComprador", LimpiarTextoXml(ObtenerNombreCliente(cliente), "CONSUMIDOR FINAL", 300)),
                new XElement("identificacionComprador", identificacionComprador),
                !string.IsNullOrWhiteSpace(contribuyenteEspecial)
                    ? new XElement("contribuyenteEspecial", contribuyenteEspecial)
                    : null,
                new XElement("obligadoContabilidad", NormalizarSiNo(emisor?.LlevaContabilidad)),
                new XElement("codDocModificado", NormalizarCodigoDocumento(notaDebito.CodDocModificado)),
                new XElement("numDocModificado", FormatearDocModificadoXml(facturaOriginal?.Serie, notaDebito.NumDocModificado)),
                new XElement(
                    "fechaEmisionDocSustento",
                    (notaDebito.FechaEmiDocModificado ?? facturaOriginal?.Fechaentrega ?? facturaOriginal?.Fchautorizacion ?? fechaEmision)
                        .ToString("dd/MM/yyyy", cultura)),
                new XElement("totalSinImpuestos", Red2(notaDebito.Subtotal ?? 0m).ToString("F2", cultura)),
                new XElement("impuestos",
                    gruposImpuesto.Select(g => new XElement("impuesto",
                        new XElement("codigo", g.Codigo),
                        new XElement("codigoPorcentaje", g.CodigoPorcentaje),
                        new XElement("tarifa", g.Tarifa.ToString("F2", cultura)),
                        new XElement("baseImponible", g.BaseImponible.ToString("F2", cultura)),
                        new XElement("valor", g.Valor.ToString("F2", cultura))
                    ))),
                new XElement("valorTotal", Red2(notaDebito.ValorTotal ?? 0m).ToString("F2", cultura)),
                new XElement("pagos",
                    new XElement("pago",
                        new XElement("formaPago", formaPago),
                        new XElement("total", Red2(notaDebito.ValorTotal ?? 0m).ToString("F2", cultura)),
                        plazo.HasValue && plazo.Value > 0 ? new XElement("plazo", plazo.Value.ToString(CultureInfo.InvariantCulture)) : null,
                        plazo.HasValue && plazo.Value > 0 ? new XElement("unidadTiempo", "dias") : null
                    ))
            ),

            new XElement("motivos",
                detalles
                    .Where(d => d.Subtotal > 0m)
                    .Select(d => new XElement("motivo",
                        new XElement("razon", LimpiarTextoXml(d.Descripcion, "Ajuste de valores", 300)),
                        new XElement("valor", Red2(d.Subtotal).ToString("F2", cultura))
                    ))),

            infoAdicional.Any()
                ? new XElement("infoAdicional", infoAdicional)
                : null
        );

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + Environment.NewLine + xml.ToString(SaveOptions.DisableFormatting);
    }

    private async Task GuardarXmlEnServidor(string contenido, string numeroNotaDebito, string ruc)
    {
        var folderPath = Path.Combine(ObtenerWebRootPath(), "notas_de_debito");
        Directory.CreateDirectory(folderPath);

        var fileName = $"{LimpiarSegmentoArchivo(ruc, "nota_debito")}_05_{LimpiarSecuencial(numeroNotaDebito)}.xml";
        var fullPath = Path.Combine(folderPath, fileName);

        await File.WriteAllTextAsync(fullPath, contenido, new UTF8Encoding(false));
    }

    private string ObtenerWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            return _env.WebRootPath;

        return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }

    private string ConstruirXmlUrl(string numNotaDebito, string ruc)
    {
        var fileName = $"{LimpiarSegmentoArchivo(ruc, "nota_debito")}_05_{LimpiarSecuencial(numNotaDebito)}.xml";
        return $"/notas_de_debito/{fileName}";
    }

    private string ConstruirXmlPath(string numNotaDebito, string ruc)
    {
        var folderPath = Path.Combine(ObtenerWebRootPath(), "notas_de_debito");
        var fileName = $"{LimpiarSegmentoArchivo(ruc, "nota_debito")}_05_{LimpiarSecuencial(numNotaDebito)}.xml";
        return Path.Combine(folderPath, fileName);
    }

    private string ConstruirPdfUrl(string numNotaDebito, string ruc, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var fileName = $"{LimpiarSegmentoArchivo(ruc, "nota_debito")}_05_{LimpiarSecuencial(numNotaDebito)}{formato.ObtenerSufijoArchivo()}.pdf";
        return $"/notas_de_debito/{fileName}";
    }

    private string ConstruirPdfPath(string numNotaDebito, string ruc, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var folderPath = Path.Combine(ObtenerWebRootPath(), "notas_de_debito");
        var fileName = $"{LimpiarSegmentoArchivo(ruc, "nota_debito")}_05_{LimpiarSecuencial(numNotaDebito)}{formato.ObtenerSufijoArchivo()}.pdf";
        return Path.Combine(folderPath, fileName);
    }

    private static string ObtenerNombreCliente(Cliente? cliente)
    {
        if (cliente == null)
            return "Cliente";

        if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
            return cliente.Nombrerazonsocial.Trim();

        var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
        return string.IsNullOrWhiteSpace(nombre) ? "Cliente" : nombre;
    }

    private static string ObtenerEstablecimiento(string serieLimpia, Emisor? emisor)
    {
        if (serieLimpia.Length >= 3)
            return serieLimpia[..3];

        if (!string.IsNullOrWhiteSpace(emisor?.CodEstablecimiento))
            return emisor.CodEstablecimiento.Trim().PadLeft(3, '0');

        return "001";
    }

    private static string ObtenerPuntoEmision(string serieLimpia, Emisor? emisor)
    {
        if (serieLimpia.Length >= 6)
            return serieLimpia.Substring(3, 3);

        if (!string.IsNullOrWhiteSpace(emisor?.CodPuntoEmision))
            return emisor.CodPuntoEmision.Trim().PadLeft(3, '0');

        return "001";
    }

    private static string LimpiarSerie(string? serie)
    {
        var limpia = (serie ?? string.Empty).Replace("-", string.Empty).Trim();
        return limpia.Length >= 6 ? limpia[..6] : limpia.PadLeft(6, '0');
    }

    private static string LimpiarSecuencial(string? secuencial)
        => (secuencial ?? string.Empty).Replace("-", string.Empty).Trim().PadLeft(9, '0');

    private static string LimpiarSegmentoArchivo(string? valor, string reemplazo)
    {
        var limpio = string.IsNullOrWhiteSpace(valor) ? reemplazo : valor.Trim();
        foreach (var caracter in Path.GetInvalidFileNameChars())
            limpio = limpio.Replace(caracter, '_');

        return limpio.Replace(" ", "_");
    }

    private static string GenerarClaveAcceso(
        DateTime fecha,
        string? ruc,
        string ambiente,
        string? serie,
        string? secuencial,
        string codDocumento,
        string tipoEmision)
    {
        var fechaStr = fecha.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
        var rucLimpio = SoloDigitos(ruc).PadLeft(13, '0');
        var serieLimpia = SoloDigitos(serie).PadLeft(6, '0');
        var secuencialLimpio = SoloDigitos(secuencial).PadLeft(9, '0');
        const string codigoNumerico = "12345678";

        var clave48 = fechaStr + codDocumento + rucLimpio + ambiente + serieLimpia + secuencialLimpio + codigoNumerico + tipoEmision;

        var suma = 0;
        var factor = 2;
        for (var i = clave48.Length - 1; i >= 0; i--)
        {
            suma += (int)char.GetNumericValue(clave48[i]) * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var verificador = 11 - (suma % 11);
        if (verificador == 11)
            verificador = 0;
        if (verificador == 10)
            verificador = 1;

        return clave48 + verificador.ToString(CultureInfo.InvariantCulture);
    }

    private static decimal Red2(decimal valor)
        => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static void NormalizarYValidarNotaDebito(NotaDebito nota, IReadOnlyCollection<DetalleNdDto> detalles)
    {
        var serie = LimpiarSerie(nota.Serie);
        if (serie.Length != 6 || !serie.All(char.IsDigit))
            throw new InvalidOperationException("La serie de la nota de debito debe tener 6 digitos.");

        var secuencial = new string((nota.NumNotaDebito ?? string.Empty).Where(char.IsDigit).ToArray());
        if (secuencial.Length > 9 || !int.TryParse(secuencial, out var numero) || numero <= 0)
            throw new InvalidOperationException("El secuencial de la nota de debito no es valido.");

        nota.Serie = serie;
        nota.NumNotaDebito = numero.ToString("000000000", CultureInfo.InvariantCulture);
        nota.CodDocumento = "05";
        nota.CodDocModificado = string.IsNullOrWhiteSpace(nota.CodDocModificado) ? "01" : nota.CodDocModificado.Trim();

        var documentoModificado = new string((nota.NumDocModificado ?? string.Empty).Where(char.IsDigit).ToArray());
        if (documentoModificado.Length != 15 && !(nota.IdDocModificado is > 0 && documentoModificado.Length == 9))
            throw new InvalidOperationException("El documento modificado debe tener establecimiento, punto de emision y secuencial.");
        if (!nota.FechaEmiDocModificado.HasValue)
            throw new InvalidOperationException("La fecha de emision del documento modificado es obligatoria.");
        nota.Ambiente = 2;

        foreach (var detalle in detalles)
        {
            detalle.Descripcion = (detalle.Descripcion ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(detalle.Descripcion) || detalle.Descripcion.Length > 300)
                throw new InvalidOperationException("Cada motivo debe tener una descripcion de hasta 300 caracteres.");
            if (detalle.Cantidad <= 0m || detalle.Preciounitario <= 0m)
                throw new InvalidOperationException("La cantidad y el valor unitario de cada motivo deben ser mayores a cero.");

            var bruto = Red2(detalle.Cantidad * detalle.Preciounitario);
            if (detalle.Descuento < 0m || detalle.Descuento > bruto)
                throw new InvalidOperationException($"El descuento del motivo '{detalle.Descripcion}' no es valido.");

            var codigoPorcentaje = NormalizarCodigoPorcentajeSri(detalle);
            if (codigoPorcentaje is not ("0" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "10"))
                throw new InvalidOperationException($"La tarifa de IVA del motivo '{detalle.Descripcion}' no es valida para el SRI.");
        }
    }

    private static void RecalcularTotales(NotaDebito nota, IEnumerable<DetalleNdDto> detalles)
    {
        decimal subtotal = 0m;
        decimal subtotalGravado = 0m;
        decimal subtotalCero = 0m;
        decimal noObjeto = 0m;
        decimal exento = 0m;
        decimal descuentos = 0m;
        decimal iva = 0m;
        decimal ice = 0m;

        foreach (var detalle in detalles)
        {
            var bruto = Red2(detalle.Cantidad * detalle.Preciounitario);
            detalle.Descuento = Red2(detalle.Descuento);
            detalle.Subtotal = Red2(bruto - detalle.Descuento);
            detalle.CodigoPorcentajeSri = NormalizarCodigoPorcentajeSri(detalle);
            var tarifa = ObtenerTarifaDesdeCodigoPorcentajeSri(detalle.CodigoPorcentajeSri);
            detalle.Iva = (int)tarifa;
            var ivaLinea = Red2(detalle.Subtotal * tarifa / 100m);
            detalle.ValorIce = Red2(Math.Max(0m, detalle.ValorIce));
            detalle.Total = Red2(detalle.Subtotal + ivaLinea + detalle.ValorIce);

            subtotal += detalle.Subtotal;
            descuentos += detalle.Descuento;
            iva += ivaLinea;
            ice += detalle.ValorIce;
            switch (detalle.CodigoPorcentajeSri)
            {
                case "6": noObjeto += detalle.Subtotal; break;
                case "7": exento += detalle.Subtotal; break;
                case "0": subtotalCero += detalle.Subtotal; break;
                default: subtotalGravado += detalle.Subtotal; break;
            }
        }

        nota.Subtotal = Red2(subtotal);
        nota.Subtotal12 = Red2(subtotalGravado);
        nota.Subtotal0 = Red2(subtotalCero);
        nota.NoImp = Red2(noObjeto);
        nota.ExIva = Red2(exento);
        nota.Descuentos = Red2(descuentos);
        nota.Iva = Red2(iva);
        nota.ValorTotal = Red2(subtotal + iva + ice);
        nota.Motivo = detalles.FirstOrDefault()?.Descripcion;

        if (nota.Subtotal <= 0m || nota.ValorTotal <= 0m)
            throw new InvalidOperationException("Los valores de la nota de debito deben ser mayores a cero.");
    }

    private static void ValidarEmisorSri(Emisor? emisor)
    {
        if (emisor == null)
            throw new InvalidOperationException("No se encontro el emisor de la nota de debito.");
        var ruc = SoloDigitos(emisor.Ruc);
        if (ruc.Length != 13 || !ruc.EndsWith("001", StringComparison.Ordinal))
            throw new InvalidOperationException("El RUC del emisor debe tener 13 digitos y terminar en 001.");
        if (string.IsNullOrWhiteSpace(emisor.RazonSocial) || string.IsNullOrWhiteSpace(emisor.DireccionMatriz ?? emisor.Direccion))
            throw new InvalidOperationException("El emisor debe tener razon social y direccion matriz configuradas.");
    }

    private static mensajeSRI CrearErrorSri(string mensaje) => new()
    {
        estado = "ERROR",
        mensaje = mensaje
    };

    private static async Task<mensajeSRI> RegistrarErrorSriAsync(
        AppDbContext db,
        NotaDebito nota,
        string mensaje)
    {
        nota.Autorizado = "0";
        nota.Mensaje = mensaje;
        nota.FechaAutoSri = DateTime.Now.ToString("O");
        await db.SaveChangesAsync();
        return CrearErrorSri(mensaje);
    }

    private static bool NotaDebitoEstaAutorizada(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim();
        return valor == "1"
            || valor.Equals("true", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("t", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("s", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("a", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("autorizado", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EstadoActivo(string? estado)
        => !string.Equals((estado ?? string.Empty).Trim(), "I", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<FacturaBusquedaDto>> FiltrarFacturasConSaldoDisponibleAsync(
        AppDbContext db,
        List<FacturaBusquedaDto> candidatos)
    {
        if (!candidatos.Any())
            return candidatos;

        var facturaIds = candidatos.Select(x => x.Codfactura).Distinct().ToList();

        var detallesFactura = await db.Detallefacturas
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
            from nc in db.NotaCreditos.AsNoTracking()
            join dnc in db.DetallesNotaCredito.AsNoTracking() on nc.Sec equals dnc.CodNotaCredito
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

    private static string FormatearNumeroCompleto(string? serie, string? secuencial)
    {
        var s = (serie ?? string.Empty).Replace("-", string.Empty).Trim();
        var n = (secuencial ?? string.Empty).Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private static string FormatearDocModificado(string? serie, string? numDoc)
    {
        if (!string.IsNullOrWhiteSpace(numDoc) && numDoc.Contains('-'))
            return numDoc.Trim();

        var s = (serie ?? string.Empty).Replace("-", string.Empty).Trim();
        var n = (numDoc ?? string.Empty).Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private static string FormatearDocModificadoXml(string? serie, string? numDoc)
    {
        var documento = SoloDigitos(numDoc);
        if (documento.Length == 15)
            return $"{documento[..3]}-{documento.Substring(3, 3)}-{documento[6..]}";

        var serieLimpia = SoloDigitos(serie);
        if (serieLimpia.Length == 6 && documento.Length <= 9)
            return $"{serieLimpia[..3]}-{serieLimpia.Substring(3, 3)}-{documento.PadLeft(9, '0')}";

        throw new InvalidOperationException("El numero del documento modificado no cumple el formato 000-000-000000000 del SRI.");
    }

    private static string SoloDigitos(string? valor)
        => new((valor ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string LimpiarTextoXml(string? valor, string reemplazo, int longitudMaxima)
    {
        var limpio = string.Join(
            " ",
            (valor ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(limpio))
            limpio = reemplazo;

        return limpio.Length <= longitudMaxima ? limpio : limpio[..longitudMaxima];
    }

    private static string NormalizarTipoEmisionSri(string? tipoEmision)
        => tipoEmision?.Trim() is "1" or "2" ? tipoEmision.Trim() : "1";

    private static string NormalizarFormaPagoSri(string? formaPago)
    {
        var digitos = SoloDigitos(formaPago);
        if (int.TryParse(digitos, out var codigo) && codigo is >= 1 and <= 21)
            return codigo.ToString("00", CultureInfo.InvariantCulture);

        return "20";
    }

    private static string NormalizarTipoIdentificacionSri(string? tipo, string identificacion)
    {
        var codigo = SoloDigitos(tipo);
        if (codigo is "04" or "05" or "06" or "07" or "08")
            return codigo;

        var identificacionNumerica = SoloDigitos(identificacion);
        if (identificacionNumerica == "9999999999999")
            return "07";

        return identificacionNumerica.Length switch
        {
            13 => "04",
            10 => "05",
            _ => "06"
        };
    }

    private static string NormalizarContribuyenteEspecial(string? valor)
    {
        var limpio = new string((valor ?? string.Empty).Where(c =>
            c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z').ToArray());
        return limpio.Length is >= 3 and <= 13 ? limpio : string.Empty;
    }

    private static string NormalizarSiNo(string? valor)
    {
        var normalizado = LimpiarTextoXml(valor, "NO", 10)
            .Normalize(NormalizationForm.FormD);
        normalizado = new string(normalizado.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return normalizado.Equals("SI", StringComparison.OrdinalIgnoreCase) ? "SI" : "NO";
    }

    private static string NormalizarCodigoDocumento(string? valor)
    {
        var codigo = SoloDigitos(valor);
        return codigo.Length == 2 ? codigo : "01";
    }

    private static string ObtenerCodigoPorcentajeSri(int iva)
    {
        return iva switch
        {
            0 => "0",
            5 => "5",
            8 => "8",
            12 => "2",
            13 => "10",
            14 => "3",
            15 => "4",
            _ => "2"
        };
    }

    private static decimal ObtenerTarifaDesdeCodigoPorcentajeSri(string? codigoPorcentaje)
    {
        return (codigoPorcentaje ?? string.Empty).Trim() switch
        {
            "0" => 0m,
            "2" => 12m,
            "3" => 14m,
            "4" => 15m,
            "5" => 5m,
            "6" => 0m,
            "7" => 0m,
            "8" => 8m,
            "10" => 13m,
            _ => 0m
        };
    }

    private static string NormalizarCodigoPorcentajeSri(DetalleNdDto detalle)
    {
        if (!string.IsNullOrWhiteSpace(detalle.CodigoPorcentajeSri))
            return detalle.CodigoPorcentajeSri.Trim();

        return ObtenerCodigoPorcentajeSri(Math.Max(0, detalle.Iva));
    }

    private static List<ImpuestoNotaDebitoDto> ConstruirGruposImpuesto(NotaDebito notaDebito, IEnumerable<DetalleNdDto> detalles)
    {
        var grupos = detalles
            .GroupBy(d =>
            {
                var codigoPorcentaje = NormalizarCodigoPorcentajeSri(d);
                var tarifaDesdeCodigo = ObtenerTarifaDesdeCodigoPorcentajeSri(codigoPorcentaje);

                return new
                {
                    Codigo = (d.CodigoImp is 2 or 3 or 5 ? d.CodigoImp : 2).ToString(CultureInfo.InvariantCulture),
                    CodigoPorcentaje = codigoPorcentaje,
                    Tarifa = tarifaDesdeCodigo > 0m ? tarifaDesdeCodigo : Math.Max(0, d.Iva)
                };
            })
            .Select(g => new ImpuestoNotaDebitoDto
            {
                Codigo = g.Key.Codigo,
                CodigoPorcentaje = g.Key.CodigoPorcentaje,
                Tarifa = g.Key.Tarifa,
                BaseImponible = Red2(g.Sum(x => Math.Max(0m, x.Subtotal))),
                Valor = Red2(g.Sum(x =>
                {
                    var tarifa = ObtenerTarifaDesdeCodigoPorcentajeSri(NormalizarCodigoPorcentajeSri(x));
                    if (tarifa <= 0m)
                        tarifa = Math.Max(0, x.Iva);
                    return Math.Max(0m, x.Subtotal) * tarifa / 100m;
                }))
            })
            .Where(x => x.BaseImponible > 0m || x.Valor > 0m)
            .ToList();

        var detallesConIce = detalles.Where(d => d.ValorIce > 0m).ToList();
        if (detallesConIce.Any())
        {
            AgregarOActualizarGrupo(
                grupos,
                "3",
                "0",
                0m,
                detallesConIce.Sum(d => Math.Max(0m, d.Subtotal)),
                detallesConIce.Sum(d => Math.Max(0m, d.ValorIce)));
        }

        AgregarOActualizarGrupo(grupos, "2", "0", 0m, notaDebito.Subtotal0 ?? 0m, 0m);
        AgregarOActualizarGrupo(grupos, "2", "6", 0m, notaDebito.NoImp ?? 0m, 0m);
        AgregarOActualizarGrupo(grupos, "2", "7", 0m, notaDebito.ExIva ?? 0m, 0m);

        if (!grupos.Any())
        {
            var subtotalGravado = Red2((notaDebito.Subtotal ?? 0m) - (notaDebito.Subtotal0 ?? 0m) - (notaDebito.NoImp ?? 0m) - (notaDebito.ExIva ?? 0m));
            var iva = Red2(notaDebito.Iva ?? 0m);

            if (subtotalGravado > 0m || iva > 0m)
            {
                var tarifa = subtotalGravado > 0m
                    ? Math.Round((iva / subtotalGravado) * 100m, 0, MidpointRounding.AwayFromZero)
                    : 0m;

                grupos.Add(new ImpuestoNotaDebitoDto
                {
                    Codigo = "2",
                    CodigoPorcentaje = ObtenerCodigoPorcentajeSri((int)tarifa),
                    Tarifa = tarifa,
                    BaseImponible = subtotalGravado,
                    Valor = iva
                });
            }

            AgregarOActualizarGrupo(grupos, "2", "0", 0m, notaDebito.Subtotal0 ?? 0m, 0m);
            AgregarOActualizarGrupo(grupos, "2", "6", 0m, notaDebito.NoImp ?? 0m, 0m);
            AgregarOActualizarGrupo(grupos, "2", "7", 0m, notaDebito.ExIva ?? 0m, 0m);
        }

        return grupos;
    }

    private static void AgregarOActualizarGrupo(
        ICollection<ImpuestoNotaDebitoDto> grupos,
        string codigo,
        string codigoPorcentaje,
        decimal tarifa,
        decimal baseImponible,
        decimal valor)
    {
        baseImponible = Red2(baseImponible);
        valor = Red2(valor);

        if (baseImponible <= 0m && valor <= 0m)
            return;

        var existente = grupos.FirstOrDefault(x => x.Codigo == codigo && x.CodigoPorcentaje == codigoPorcentaje);
        if (existente == null)
        {
            grupos.Add(new ImpuestoNotaDebitoDto
            {
                Codigo = codigo,
                CodigoPorcentaje = codigoPorcentaje,
                Tarifa = tarifa,
                BaseImponible = baseImponible,
                Valor = valor
            });

            return;
        }

        existente.BaseImponible = Math.Max(existente.BaseImponible, baseImponible);
        existente.Valor = Math.Max(existente.Valor, valor);
        existente.Tarifa = tarifa;
    }

    private static DateTime ObtenerFechaEmision(NotaDebito notaDebito)
    {
        return TryObtenerFechaDesdeClaveAcceso(notaDebito.CodClave, out var fecha)
            ? fecha
            : DateTime.Today;
    }

    private static bool TryObtenerFechaDesdeClaveAcceso(string? claveAcceso, out DateTime fecha)
    {
        fecha = default;

        if (string.IsNullOrWhiteSpace(claveAcceso))
            return false;

        var clave = claveAcceso.Trim();
        if (clave.Length < 8)
            return false;

        return DateTime.TryParseExact(
            clave[..8],
            "ddMMyyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out fecha);
    }

    private static bool AsegurarClaveAcceso(NotaDebito notaDebito, Emisor? emisor)
    {
        var fechaEmision = ObtenerFechaEmision(notaDebito);
        const string ambiente = "2";
        var serieLimpia = LimpiarSerie(notaDebito.Serie);
        var secuencial = (notaDebito.NumNotaDebito ?? "1").Trim().PadLeft(9, '0');
        var tipoEmision = NormalizarTipoEmisionSri(emisor?.TipoEmision);

        var claveEsperada = GenerarClaveAcceso(
            fechaEmision,
            emisor?.Ruc,
            ambiente,
            serieLimpia,
            secuencial,
            "05",
            tipoEmision);

        if (string.Equals(notaDebito.CodClave?.Trim(), claveEsperada, StringComparison.Ordinal))
            return false;

        notaDebito.CodClave = claveEsperada;

        return true;
    }

    private static async Task<bool> XmlNotaDebitoNecesitaRegeneracionAsync(string rutaXml)
    {
        if (!File.Exists(rutaXml))
            return true;

        var contenido = await File.ReadAllTextAsync(rutaXml, Encoding.UTF8);
        return !contenido.TrimStart().StartsWith("<?xml", StringComparison.Ordinal);
    }
}
