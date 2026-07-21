using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Simetric.Services;

public class NotaCreditoService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly INotaCreditoPdfService _notaCreditoPdfService;
    private readonly ICajaSerieResolver _cajaSerieResolver;
    private readonly EmisionControlService _emisionControlService;
    private readonly InitialSequencePromptService _initialSequencePromptService;
    private readonly SriXmlProcessorService _sriXmlProcessorService;
    private readonly EmisorCertificadoProtector _certificadoProtector;
    private readonly EmisorSistemaService _emisorSistemaService;

    public NotaCreditoService(
        IDbContextFactory<AppDbContext> dbFactory,
        IWebHostEnvironment env,
        IEmailService emailService,
        INotaCreditoPdfService notaCreditoPdfService,
        ICajaSerieResolver cajaSerieResolver,
        EmisionControlService emisionControlService,
        InitialSequencePromptService initialSequencePromptService,
        SriXmlProcessorService sriXmlProcessorService,
        EmisorCertificadoProtector certificadoProtector,
        EmisorSistemaService emisorSistemaService)
    {
        _dbFactory = dbFactory;
        _env = env;
        _emailService = emailService;
        _notaCreditoPdfService = notaCreditoPdfService;
        _cajaSerieResolver = cajaSerieResolver;
        _emisionControlService = emisionControlService;
        _initialSequencePromptService = initialSequencePromptService;
        _sriXmlProcessorService = sriXmlProcessorService;
        _certificadoProtector = certificadoProtector;
        _emisorSistemaService = emisorSistemaService;
    }

    private async Task<CajaSerieResolucion> ResolverSerieNotaCreditoAsync(int userId, string? serieSolicitadaRaw = null)
    {
        if (!string.IsNullOrWhiteSpace(serieSolicitadaRaw))
            return await _cajaSerieResolver.ResolverAsync(userId, serieSolicitadaRaw);

        var resolucionBase = await _cajaSerieResolver.ResolverAsync(userId);
        var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
            userId,
            "nota-credito",
            resolucionBase.SerieRaw);

        if (!string.IsNullOrWhiteSpace(seriePreferida) &&
            !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
        {
            return await _cajaSerieResolver.ResolverAsync(userId, seriePreferida);
        }

        return resolucionBase;
    }

    public sealed class EmisionNotaCreditoAutomaticaResultado
    {
        public bool Success { get; set; }
        public bool Autorizada { get; set; }
        public int? Sec { get; set; }
        public string NumeroNotaCredito { get; set; } = string.Empty;
        public string NumeroCompleto { get; set; } = string.Empty;
        public string NumeroAutorizacion { get; set; } = string.Empty;
        public string PdfUrl { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string EstadoSri { get; set; } = string.Empty;
    }

    public sealed class EmisionSriNotaCreditoResultado
    {
        public bool Success { get; set; }
        public bool Autorizada { get; set; }
        public int Sec { get; set; }
        public string EstadoSri { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string RutaXml { get; set; } = string.Empty;
        public mensajeSRI RespuestaSri { get; set; } = new();
        public FacturaCorreoEnvioResultadoDto? ResultadoCorreo { get; set; }
    }

    private sealed class NotaCreditoCorreoMetadata
    {
        public List<string> Destinatarios { get; set; } = new();
        public bool CorreoEnviado { get; set; }
        public DateTime? FechaEnvioCorreo { get; set; }
        public string? UltimoErrorCorreo { get; set; }
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

    private static List<string> ParseCorreosDocumento(string? correoad)
    {
        if (string.IsNullOrWhiteSpace(correoad))
            return new List<string>();

        return NormalizarCorreos(
            correoad.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SerializarCorreosDocumento(IEnumerable<string> correos)
        => string.Join(";", NormalizarCorreos(correos));

    private static NotaCreditoCorreoMetadata LeerNotaCreditoCorreoMetadata(string? detalleextra)
    {
        if (string.IsNullOrWhiteSpace(detalleextra))
            return new NotaCreditoCorreoMetadata();

        try
        {
            return JsonSerializer.Deserialize<NotaCreditoCorreoMetadata>(detalleextra) ?? new NotaCreditoCorreoMetadata();
        }
        catch
        {
            return new NotaCreditoCorreoMetadata();
        }
    }

    private static string EscribirNotaCreditoCorreoMetadata(NotaCreditoCorreoMetadata metadata)
        => JsonSerializer.Serialize(metadata);

    private static string ObtenerNombreCliente(Cliente? cliente)
    {
        if (cliente == null)
            return "Cliente";

        if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
            return cliente.Nombrerazonsocial.Trim();

        var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
        return string.IsNullOrWhiteSpace(nombre) ? "Cliente" : nombre;
    }

    private static string ObtenerNumeroNotaDocumento(NotaCredito notaCredito)
    {
        var serie = notaCredito.Serie?.Trim();
        var numero = notaCredito.NumNotaCredito?.Trim() ?? notaCredito.Sec.ToString();

        return string.IsNullOrWhiteSpace(serie)
            ? numero
            : FormatearNumeroCompleto(serie, numero);
    }

    private static bool NotaCreditoEstaAutorizada(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return false;

        var valor = autorizado.Trim();
        return valor.Equals("true", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("1", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("s", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("a", StringComparison.OrdinalIgnoreCase)
            || valor.Equals("autorizado", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<FacturaBusquedaDto>> BuscarFacturasAutocompleteAsync(string texto, int idUsuario)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        texto = (texto ?? "").Trim();

        if (string.IsNullOrWhiteSpace(texto))
            return new List<FacturaBusquedaDto>();

        var candidatos = await (from f in db.Facturas.AsNoTracking()
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
                        Numfactura = f.Numfactura ?? "",
                        Serie = f.Serie ?? "",
                        ClienteNombre = c != null
                            ? (!string.IsNullOrWhiteSpace(c.Nombrerazonsocial)
                                ? c.Nombrerazonsocial
                                : ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")).Trim())
                            : ""
                    })
                    .Take(40)
                    .ToListAsync();

        return await FiltrarFacturasConSaldoDisponibleAsync(db, candidatos);
    }

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
    // Dentro de NotaCreditoService.cs

    public async Task<List<DetalleNcDto>> ObtenerDetallesDisponiblesAsync(int codFactura)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        // 1. Traer detalles de la factura original
        var detallesFactura = await db.Detallefacturas
            .Where(d => d.Codfactura == codFactura)
            .ToListAsync();

        // 2. Traer TODO lo que ya se ha anulado en Notas de Crédito previas para esta factura
        // Buscamos en la tabla DETALLENOTACREDITO uniendo con NOTACREDITO por el codFactura
        var yaAnulados = await (from nc in db.NotaCreditos
                                join dnc in db.DetallesNotaCredito on nc.Sec equals dnc.CodNotaCredito
                                where nc.IdDocModificado == codFactura
                                select dnc).ToListAsync();

        // 3. Cruzar información: Cantidad Original - Cantidad Anulada
        var resultado = detallesFactura.Select(df =>
        {
            var cantYaAnulada = yaAnulados
                .Where(a => a.CodProducto == df.Codproducto)
                .Sum(a => a.CantProducto ?? 0m);

            return new DetalleNcDto
            {
                Codproducto = df.Codproducto,
                Codprincipal = df.Codprincipal,
                Codauxiliar = df.Codauxiliar,
                Descripcion = df.Descripproducto ?? "",
                Detalle = df.Descripproducto ?? "",
                Cantidad = (df.Cantproducto) - cantYaAnulada, // Restamos
                Preciounitario = df.Precioproducto,
                Descuento = 0m,
                PorcentajeDescuento = 0m,
                Iva = df.Tarifa,
                Subtotal = ((df.Cantproducto) - cantYaAnulada) * (df.Precioproducto)
            };
        })
        .Where(r => r.Cantidad > 0) // Si la cantidad llega a 0, ya no sale en la lista
        .ToList();

        return resultado;
    }
    public async Task<int> CrearNotaAnulacionTotalAsync(NotaCredito nc, decimal totalFactura, int codProductoAnulacion)
    {
        // Creamos un único detalle genérico
        var detalleAnulacion = new List<DetalleNcDto>
    {
        new DetalleNcDto
        {
            Codproducto = codProductoAnulacion,
            Codprincipal = codProductoAnulacion.ToString(CultureInfo.InvariantCulture),
            Codauxiliar = codProductoAnulacion.ToString(CultureInfo.InvariantCulture),
            Descripcion = "ANULACION DE COMPROBANTE",
            Detalle = "ANULACION DE COMPROBANTE",
            Cantidad = 1,
            Preciounitario = totalFactura,
            Subtotal = totalFactura,
            Total = totalFactura,
            Iva = 15 // Ajustar según la tarifa que aplique la empresa
        }
    };

        return await CrearAsync(nc, detalleAnulacion); // Reutiliza tu método CrearAsync existente 
    }

    public async Task<(bool Success, string Message)> CambiarProductoNotaCreditoAsync(DetalleNcDto productoOriginal, int nuevoCodProducto)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        // Buscar el nuevo producto en la base 
        var nuevoProducto = await db.Productos
            .FirstOrDefaultAsync(p => p.Codigo == nuevoCodProducto);

        if (nuevoProducto == null) return (false, "El producto nuevo no existe.");

        // Regla: El valor debe ser el mismo
        if (nuevoProducto.ValorUnitario != productoOriginal.Preciounitario)
        {
            return (false, $"No se puede realizar el cambio. El valor del nuevo producto (${nuevoProducto.ValorUnitario}) " +
                           $"es diferente al original (${productoOriginal.Preciounitario}).");
        }

        // Si el valor es igual, se procede con la lógica de intercambio en el detalle de la NC
        productoOriginal.Codproducto = nuevoProducto.Codigo;
        productoOriginal.Codprincipal = nuevoProducto.CodigoPrincipal;
        productoOriginal.Codauxiliar = nuevoProducto.CodigoPrincipal;
        productoOriginal.Descripcion = nuevoProducto.Nombre ?? "Cambio de producto";

        return (true, "Producto cambiado exitosamente.");
    }

    public async Task<int> ResolverClienteParaNotaCreditoAsync(int idUsuario, Cliente clienteEntrada)
    {
        if (idUsuario <= 0)
            throw new InvalidOperationException("No se pudo identificar el usuario para asociar el cliente.");

        var identificacion = LimpiarTextoNotaCredito(clienteEntrada.Numeroidentificacion);
        var nombre = LimpiarTextoNotaCredito(clienteEntrada.Nombrerazonsocial)
            ?? LimpiarTextoNotaCredito($"{clienteEntrada.Nombres} {clienteEntrada.Apellidos}");

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
            var tipoIdentificacion = LimpiarTextoNotaCredito(clienteEntrada.Tipoidentificacion);
            var direccion = LimpiarTextoNotaCredito(clienteEntrada.Direccion);
            var celular = LimpiarTextoNotaCredito(clienteEntrada.Celular);
            var correo = LimpiarTextoNotaCredito(clienteEntrada.Correo);

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
            Tipoidentificacion = LimpiarTextoNotaCredito(clienteEntrada.Tipoidentificacion) ?? "05",
            Numeroidentificacion = identificacion,
            Nombrerazonsocial = nombre,
            Direccion = LimpiarTextoNotaCredito(clienteEntrada.Direccion),
            Celular = LimpiarTextoNotaCredito(clienteEntrada.Celular),
            Correo = LimpiarTextoNotaCredito(clienteEntrada.Correo),
            TipoCliente = clienteEntrada.TipoCliente
        };

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Codcliente;
    }

    private static string? LimpiarTextoNotaCredito(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    public async Task<int> CrearAsync(
     NotaCredito nc,
     List<DetalleNcDto> detalles,
     List<FacturaCorreoDestinoDto>? correosNota = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // 1. Pre-transaction validation and setup
        if (nc.Usuario is not > 0)
            throw new Exception("No se pudo identificar el usuario para asignar la serie de la nota de crédito.");

        await _emisionControlService.AsegurarPuedeEmitirAsync(nc.Usuario.Value);

        var resolucion = await ResolverSerieNotaCreditoAsync(nc.Usuario.Value, NormalizarSerieDocumento(nc.Serie));
        nc.Serie = resolucion.SerieRaw;

        var cliente = nc.CodClientes.HasValue
            ? await db.Clientes.FirstOrDefaultAsync(c => c.Codcliente == nc.CodClientes.Value)
            : null;

        var correosNotaNormalizados = NormalizarCorreos(correosNota?.Select(x => x.Correo));
        var correosGuardarEnCliente = NormalizarCorreos(
            correosNota?
                .Where(x => x.GuardarEnCliente)
                .Select(x => x.Correo));

        var destinatariosNota = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            db,
            nc.Usuario,
            cliente?.Codcliente,
            cliente?.Correo,
            correosNotaNormalizados);

        nc.Correoad = SerializarCorreosDocumento(destinatariosNota);
        nc.Detalleextra = EscribirNotaCreditoCorreoMetadata(new NotaCreditoCorreoMetadata
        {
            Destinatarios = destinatariosNota,
            CorreoEnviado = false,
            FechaEnvioCorreo = null,
            UltimoErrorCorreo = null
        });

        // 2. Define the Execution Strategy for handling retries
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            // 3. Open the transaction INSIDE the execution strategy
            await using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                db.NotaCreditos.Add(nc);
                await db.SaveChangesAsync();

                foreach (var d in detalles)
                {
                    var valorIva = Math.Round(d.Subtotal * d.Iva / 100m, 2, MidpointRounding.AwayFromZero);

                    var detalleDb = new DetalleNotaCredito
                    {
                        CodNotaCredito = nc.Sec,
                        CodProducto = d.Codproducto,
                        CodPrincipal = string.IsNullOrWhiteSpace(d.Codprincipal) ? d.Codproducto.ToString(CultureInfo.InvariantCulture) : d.Codprincipal.Trim(),
                        CodAuxiliar = string.IsNullOrWhiteSpace(d.Codauxiliar) ? d.Codprincipal : d.Codauxiliar.Trim(),
                        CantProducto = d.Cantidad,
                        DescripProducto = d.Descripcion,
                        PrecioProducto = d.Preciounitario,
                        Descuento = d.Descuento,
                        ValorTProducto = d.Subtotal,
                        ValorIVA = valorIva,
                        CodImp = 2,
                        PorImp = d.Iva,
                        Tarifa = d.Iva
                    };

                    db.DetallesNotaCredito.Add(detalleDb);
                }

                await db.SaveChangesAsync();

                if (cliente != null && correosGuardarEnCliente.Any())
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

                await _emisionControlService.ConsumirDocumentoAsync(db, nc.Usuario.Value);

                // Commit changes if everything succeeded
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                try { await transaction.RollbackAsync(); } catch { }
                throw; // Rethrow so the execution strategy knows it needs to retry (or fail)
            }
        });

        // 4. Post-transaction file / integration logic 
        var emisor = await db.Emisores.FirstOrDefaultAsync(e => e.Codigo == nc.CodEmisor);
        var xmlContent = GenerarXmlNotaCredito(nc, detalles, emisor, cliente);
        await GuardarXmlEnServidor(xmlContent, nc.NumNotaCredito ?? "", emisor?.Ruc ?? "");

        return nc.Sec;
    }

    private async Task GuardarXmlEnServidor(string contenido, string numNota, string ruc)
    {
        string folderPath = Path.Combine(_env.WebRootPath, "notas_de_credito");

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string fileName = $"{ruc}_04_{(numNota ?? "").Replace("-", "").PadLeft(9, '0')}.xml";
        string fullPath = Path.Combine(folderPath, fileName);

        await File.WriteAllTextAsync(fullPath, contenido, System.Text.Encoding.UTF8);
    }

    public async Task<(bool Success, string Message)> ValidarProductosDisponiblesAsync(int codFactura, List<DetalleNcDto> detallesSolicitados)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var detallesFactura = await db.Detallefacturas
            .Where(d => d.Codfactura == codFactura)
            .ToListAsync();

        var yaAnulados = await (
            from nc in db.NotaCreditos
            join dnc in db.DetallesNotaCredito on nc.Sec equals dnc.CodNotaCredito
            where nc.IdDocModificado == codFactura
            select dnc
        ).ToListAsync();

        foreach (var detalle in detallesSolicitados)
        {
            if (detalle.Cantidad <= 0m)
                return (false, $"La cantidad del producto '{detalle.Descripcion}' debe ser mayor a cero.");

            if (detalle.Cantidad != decimal.Truncate(detalle.Cantidad))
                return (false, $"La cantidad del producto '{detalle.Descripcion}' debe ser un número entero.");

            var original = detallesFactura.FirstOrDefault(x => x.Codproducto == detalle.Codproducto);
            if (original == null)
                return (false, $"El producto con código {detalle.Codproducto} ya no existe en la factura original.");

            var cantidadOriginal = original.Cantproducto;
            var cantidadYaAnulada = yaAnulados
                .Where(x => x.CodProducto == detalle.Codproducto)
                .Sum(x => x.CantProducto ?? 0m);

            var cantidadDisponible = cantidadOriginal - cantidadYaAnulada;

            if (cantidadDisponible <= 0)
            {
                return (false, $"El producto '{original.Descripproducto}' ya fue anulado anteriormente y no puede generar otra nota de crédito.");
            }

            if (detalle.Cantidad > cantidadDisponible)
            {
                return (false, $"La cantidad solicitada para '{original.Descripproducto}' excede la disponible. Disponible: {cantidadDisponible:0.##}.");
            }
        }

        return (true, string.Empty);
    }

    public async Task<EmisionNotaCreditoAutomaticaResultado> EmitirNotaCreditoAutomaticaDesdeFacturaAsync(
        int idUsuario,
        int codFactura,
        string? motivo = null)
    {
        if (idUsuario <= 0)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "No se pudo identificar al usuario para emitir la nota de crédito."
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var factura = await db.Facturas
            .AsNoTracking()
            .Include(f => f.CodclientesNavigation)
            .Include(f => f.CodemisorNavigation)
            .Include(f => f.Detallefacturas)
            .FirstOrDefaultAsync(f => f.Codfactura == codFactura && f.Idusuario == idUsuario);

        if (factura == null)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "La factura ya no existe o no pertenece al usuario actual."
            };
        }

        if (factura.Estado != true)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "La factura está anulada y no puede generar una nota de crédito automática."
            };
        }

        if (!DocumentoAutorizacionHelper.EstaAutorizado(factura.Autorizado, factura.Estadoenviosri))
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "Solo se pueden generar notas de crédito automáticas desde facturas autorizadas por el SRI."
            };
        }

        var resolucionNc = await ResolverSerieNotaCreditoAsync(idUsuario);
        var serieNcRaw = resolucionNc.SerieRaw;
        if (string.IsNullOrWhiteSpace(serieNcRaw))
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "Configura la serie de notas de crédito en la caja antes de emitir automáticamente."
            };
        }

        var emisor = factura.CodemisorNavigation;
        if (emisor == null)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "La factura no tiene un emisor válido asociado para generar la nota de crédito automática."
            };
        }

        var rutaCertificado = ResolverRutaCertificado(emisor);
        var claveCertificado = ResolverClaveCertificado(emisor);
        if (string.IsNullOrWhiteSpace(rutaCertificado) || !File.Exists(rutaCertificado) || string.IsNullOrWhiteSpace(claveCertificado))
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "Este emisor no tiene firma electrónica configurada, por lo cual no puede hacer la nota de crédito automática."
            };
        }

        var resolucionSecuencia = await ResolverSecuenciaAutomaticaNotaCreditoAsync(idUsuario, emisor);
        if (!string.IsNullOrWhiteSpace(resolucionSecuencia.Error))
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = resolucionSecuencia.Error
            };
        }

        serieNcRaw = resolucionSecuencia.SerieRaw;
        var secuenciaState = resolucionSecuencia.Estado;
        if (!secuenciaState.Initialized)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "Primero inicializa la secuencia de notas de crédito desde el módulo de Nota de Crédito."
            };
        }

        var siguienteAutomatico = await ObtenerSiguienteSecuencialNotaCreditoAsync(db, idUsuario, serieNcRaw, emisor.Codigo);
        var secNcRaw = _initialSequencePromptService.ResolveNextSequence(siguienteAutomatico, secuenciaState);
        if (string.IsNullOrWhiteSpace(secNcRaw))
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "No se pudo resolver el siguiente secuencial de la nota de crédito."
            };
        }

        var detallesDisponibles = await ConstruirDetallesAutomaticosDisponiblesAsync(db, codFactura);
        if (!detallesDisponibles.Any())
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = "La factura ya no tiene cantidades disponibles para generar otra nota de crédito."
            };
        }

        var validacion = await ValidarProductosDisponiblesAsync(codFactura, detallesDisponibles);
        if (!validacion.Success)
        {
            return new EmisionNotaCreditoAutomaticaResultado
            {
                Message = validacion.Message
            };
        }

        foreach (var detalle in detallesDisponibles)
        {
            detalle.Descuento = Math.Round(Math.Max(detalle.Descuento, 0m), 2, MidpointRounding.AwayFromZero);
            detalle.Subtotal = Math.Round(Math.Max(detalle.Subtotal, 0m), 2, MidpointRounding.AwayFromZero);
            var valorIva = Math.Round(detalle.Subtotal * detalle.Iva / 100m, 2, MidpointRounding.AwayFromZero);
            detalle.Total = detalle.Subtotal + valorIva;
        }

        var subtotal = detallesDisponibles.Sum(d => d.Subtotal);
        var descuentos = detallesDisponibles.Sum(d => d.Descuento);
        var iva = detallesDisponibles.Sum(d => Math.Round(d.Subtotal * d.Iva / 100m, 2, MidpointRounding.AwayFromZero));
        var total = subtotal + iva;

        var numeroFacturaVisual = FormatearNumeroCompleto(factura.Serie, factura.Numfactura);
        var numeroNotaCreditoCompleto = FormatearNumeroCompleto(serieNcRaw, secNcRaw);
        var notaCredito = new NotaCredito
        {
            Usuario = idUsuario,
            CodClientes = factura.Codclientes,
            CodEmisor = factura.Codemisor,
            CodDocumento = "04",
            Serie = serieNcRaw,
            NumNotaCredito = secNcRaw,
            IdDocModificado = factura.Codfactura,
            NumDocModificado = factura.Numfactura,
            CodDocModificado = factura.Coddocumento?.ToString(),
            FechaEmiDocModificado = factura.Fchautorizacion ?? factura.Fechaentrega ?? DateTime.Now,
            Subtotal = subtotal,
            Descuentos = descuentos,
            Iva = iva,
            ValorTotal = total,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? "ANULACION AUTOMATICA DESDE FACTURA" : motivo.Trim(),
            Observacion = $"NC automática generada desde factura {numeroFacturaVisual}",
            Estado = true,
            Autorizado = string.Empty
        };

        int secNotaCredito = 0;

        try
        {
            secNotaCredito = await CrearAsync(notaCredito, detallesDisponibles);
            await _initialSequencePromptService.UpdateLastSequenceAsync(idUsuario, "nota-credito", secNcRaw, serieNcRaw, emisor.Codigo);

            var resultadoSri = await EmitirNotaCreditoSriAsync(secNotaCredito, idUsuario);
            var pdfUrl = string.Empty;

            if (resultadoSri.Autorizada)
            {
                try
                {
                    pdfUrl = await AsegurarPdfNotaCreditoUsuarioAsync(secNotaCredito, idUsuario) ?? string.Empty;
                }
                catch
                {
                }
            }

            return new EmisionNotaCreditoAutomaticaResultado
            {
                Success = true,
                Autorizada = resultadoSri.Autorizada,
                Sec = secNotaCredito,
                NumeroNotaCredito = secNcRaw,
                NumeroCompleto = numeroNotaCreditoCompleto,
                NumeroAutorizacion = resultadoSri.RespuestaSri.autorizacion ?? string.Empty,
                PdfUrl = pdfUrl,
                EstadoSri = resultadoSri.EstadoSri,
                Message = resultadoSri.Message
            };
        }
        catch (Exception ex)
        {
            if (secNotaCredito > 0)
            {
                try
                {
                    await ActualizarAutorizacionNCAsync(secNotaCredito, string.Empty, DateTime.Now.ToString("O"), $"ERROR INTERNO: {ex.Message}", "ERROR INTERNO");
                }
                catch
                {
                }
            }

            return new EmisionNotaCreditoAutomaticaResultado
            {
                Sec = secNotaCredito > 0 ? secNotaCredito : null,
                NumeroNotaCredito = secNcRaw,
                Message = $"No se pudo emitir la nota de crédito automática: {ex.Message}"
            };
        }
    }
    public string GenerarXmlNotaCredito(NotaCredito nc, List<DetalleNcDto> detalles, Emisor? emisor, Cliente? cliente)
    {
        var cultura = CultureInfo.InvariantCulture;
        string ambiente = "2"; // 1 pruebas, 2 produccion segun tu logica real
        string serieLimpia = (nc.Serie ?? "001001").Replace("-", "");
        string secuencial = (nc.NumNotaCredito ?? "1").Trim().PadLeft(9, '0');
        string claveAcceso = ObtenerClaveAccesoNotaCredito(nc, emisor?.Ruc, ambiente, serieLimpia, secuencial);

        ValidarDatosAutorizacionNotaCredito(nc);

        var tarifaPredominante = detalles
            .GroupBy(d => d.Iva)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        string codigoPorcentaje = ObtenerCodigoPorcentajeSri(tarifaPredominante);

        //string claveAcceso = DateTime.Now.ToString("ddMMyyyy") +
        //                     "04" +
        //                     (emisor?.Ruc ?? "") +
        //                     ambiente +
        //                     serieLimpia +
        //                     secuencial +
        //                     "12345678" +
        //                     "1";
        XElement xml = new XElement("notaCredito",
            new XAttribute("id", "comprobante"),
            new XAttribute("version", "1.1.0"),

            new XElement("infoTributaria",
                new XElement("ambiente", ambiente),
                new XElement("tipoEmision", "1"),
                new XElement("razonSocial", emisor?.RazonSocial ?? "-"),
                new XElement("nombreComercial", emisor?.NomComercial ?? "-"),
                new XElement("ruc", emisor?.Ruc ?? ""),
                new XElement("claveAcceso", claveAcceso),
                new XElement("codDoc", "04"),
                new XElement("estab", serieLimpia.Length >= 3 ? serieLimpia.Substring(0, 3) : "001"),
                new XElement("ptoEmi", serieLimpia.Length >= 6 ? serieLimpia.Substring(3, 3) : "001"),
                new XElement("secuencial", secuencial),
                new XElement("dirMatriz", emisor?.DireccionMatriz ?? "")
            ),

            new XElement("infoNotaCredito",
                new XElement("fechaEmision", DateTime.Now.ToString("dd/MM/yyyy")),
                new XElement("dirEstablecimiento", emisor?.DireccionMatriz ?? ""),
                new XElement("tipoIdentificacionComprador", cliente?.Tipoidentificacion ?? "04"),
                new XElement("razonSocialComprador",
                    !string.IsNullOrWhiteSpace(cliente?.Nombrerazonsocial)
                        ? cliente!.Nombrerazonsocial
                        : ((cliente?.Nombres ?? "") + " " + (cliente?.Apellidos ?? "")).Trim()),
                new XElement("identificacionComprador", cliente?.Numeroidentificacion ?? ""),
                new XElement("obligadoContabilidad", cliente?.Oblgconta ?? "NO"),
                new XElement("codDocModificado", "01"),
                new XElement("numDocModificado", FormatearDocumentoModificadoXml(nc.Serie, nc.NumDocModificado)),
                new XElement("fechaEmisionDocSustento", nc.FechaEmiDocModificado?.ToString("dd/MM/yyyy") ?? DateTime.Now.ToString("dd/MM/yyyy")),
                new XElement("totalSinImpuestos", (nc.Subtotal ?? 0m).ToString("F2", cultura)),
                new XElement("valorModificacion", (nc.ValorTotal ?? 0m).ToString("F2", cultura)),
                new XElement("moneda", "DOLAR"),

                new XElement("totalConImpuestos",
                    new XElement("totalImpuesto",
                        new XElement("codigo", "2"),
                        new XElement("codigoPorcentaje", codigoPorcentaje),
                        new XElement("baseImponible", (nc.Subtotal ?? 0m).ToString("F2", cultura)),
                        new XElement("valor", (nc.Iva ?? 0m).ToString("F2", cultura))
                    )
                ),

                new XElement("motivo", nc.Motivo ?? "ANULACION")
            ),

            new XElement("detalles",
                detalles.Select(d =>
                {
                    decimal valorIvaLinea = Math.Round(d.Subtotal * d.Iva / 100m, 2, MidpointRounding.AwayFromZero);

                    var codigoInterno = string.IsNullOrWhiteSpace(d.Codprincipal)
                        ? d.Codproducto.ToString(CultureInfo.InvariantCulture)
                        : d.Codprincipal.Trim();

                    return new XElement("detalle",
                        new XElement("codigoInterno", codigoInterno),
                        string.IsNullOrWhiteSpace(d.Codauxiliar)
                            ? null
                            : new XElement("codigoAdicional", d.Codauxiliar.Trim()),
                        new XElement("descripcion", d.Descripcion ?? ""),
                        new XElement("cantidad", d.Cantidad.ToString("F6", cultura)),
                        new XElement("precioUnitario", d.Preciounitario.ToString("F6", cultura)),
                        new XElement("descuento", d.Descuento.ToString("F2", cultura)),
                        new XElement("precioTotalSinImpuesto", d.Subtotal.ToString("F2", cultura)),
                        string.IsNullOrWhiteSpace(d.Detalle)
                            ? null
                            : new XElement("detallesAdicionales",
                                new XElement("detAdicional",
                                    new XAttribute("nombre", "Detalle"),
                                    new XAttribute("valor", d.Detalle.Trim()))),
                        new XElement("impuestos",
                            new XElement("impuesto",
                                new XElement("codigo", "2"),
                                new XElement("codigoPorcentaje", ObtenerCodigoPorcentajeSri(d.Iva)),
                                new XElement("tarifa", d.Iva.ToString("F2", cultura)),
                                new XElement("baseImponible", d.Subtotal.ToString("F2", cultura)),
                                new XElement("valor", valorIvaLinea.ToString("F2", cultura))
                            )
                        )
                    );
                })
            ),

            new XElement("infoAdicional",
                new XElement("campoAdicional",
                    new XAttribute("nombre", "Email"),
                    cliente?.Correo ?? "cliente@correo.com"),
                new XElement("campoAdicional",
                    new XAttribute("nombre", "Motivo"),
                    nc.Motivo ?? "-"),
                new XElement("campoAdicional",
                    new XAttribute("nombre", "Observacion"),
                    nc.Observacion ?? "-"),
                NotaCreditoEstaAutorizada(nc.Autorizado)
                    ? new XElement("campoAdicional",
                        new XAttribute("nombre", "ClaveAcceso"),
                        nc.CodClave ?? claveAcceso)
                    : null,
                NotaCreditoEstaAutorizada(nc.Autorizado)
                    ? new XElement("campoAdicional",
                        new XAttribute("nombre", "NumeroAutorizacion"),
                        nc.NumAutorizacion ?? string.Empty)
                    : null,
                NotaCreditoEstaAutorizada(nc.Autorizado)
                    ? new XElement("campoAdicional",
                        new XAttribute("nombre", "FechaAutorizacion"),
                        (nc.FchAutorizacion ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm:ss"))
                    : null
            )
        );

        return xml.ToString();
    }

    public async Task<List<NotaCreditoListDto>> ListarNotasCreditoUsuarioAsync(int idUsuario)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var data = await (
            from nc in db.NotaCreditos.AsNoTracking()
            join c in db.Clientes.AsNoTracking()
                on nc.CodClientes equals c.Codcliente into cliJoin
            from c in cliJoin.DefaultIfEmpty()
            join f in db.Facturas.AsNoTracking()
                on nc.IdDocModificado equals f.Codfactura into facturaJoin
            from f in facturaJoin.DefaultIfEmpty()

            join e in db.Emisores.AsNoTracking()
                on nc.CodEmisor equals e.Codigo into emiJoin
            from e in emiJoin.DefaultIfEmpty()

            join ti in db.Identificacion.AsNoTracking()
                on c.Tipoidentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()

            where nc.Usuario == idUsuario &&
                  (e == null || e.EsEmisorSistema != true)
            orderby nc.Sec descending
            select new
            {
                Sec = nc.Sec,
                NumeroNotaCredito = nc.NumNotaCredito ?? "",
                Serie = nc.Serie ?? "",
                Cliente = c != null
                    ? (!string.IsNullOrWhiteSpace(c.Nombrerazonsocial)
                        ? c.Nombrerazonsocial
                        : ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")).Trim())
                    : "",
                IdentificacionCliente = c != null ? (c.Numeroidentificacion ?? "") : "",
                TipoIdentificacionCliente = ti != null ? (ti.IdeDescripcion ?? "") : "",
                NumeroDocModificado = nc.NumDocModificado ?? "",
                FechaDocumentoModificado = nc.FechaEmiDocModificado,
                Subtotal = nc.Subtotal ?? 0m,
                Iva = nc.Iva ?? 0m,
                Total = nc.ValorTotal ?? 0m,
                Motivo = nc.Motivo ?? "",
                Estado = nc.Estado ?? false,
                Autorizado = nc.Autorizado ?? "",
                NumeroAutorizacion = nc.NumAutorizacion ?? "",
                MensajeSri = nc.Observacion ?? "",
                FechaAutorizacion = nc.FchAutorizacion,
                ClaveAcceso = nc.CodClave ?? "",
                RucEmisor = e != null ? (e.Ruc ?? "") : "",
                FechaVencimientoDocumento = f != null
                    ? (f.Fechavence
                        ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                            ? (c != null && c.DiasCredito.HasValue
                                ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(c.DiasCredito.Value)
                                : (DateTime?)null)
                            : null))
                    : null,
                SaldoPendienteDocumento = f != null
                    ? ((f.Valortotal ?? 0m) - (db.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0m))
                    : 0m
            })
            .ToListAsync();

        var resultado = data.Select(x => new NotaCreditoListDto
        {
            Sec = x.Sec,
            NumeroNotaCredito = x.NumeroNotaCredito,
            Serie = x.Serie,
            Cliente = x.Cliente,
            IdentificacionCliente = x.IdentificacionCliente,
            TipoIdentificacionCliente = x.TipoIdentificacionCliente,
            NumeroDocModificado = x.NumeroDocModificado,
            FechaDocumentoModificado = x.FechaDocumentoModificado,
            Subtotal = x.Subtotal,
            Iva = x.Iva,
            Total = x.Total,
            Motivo = x.Motivo,
            Estado = x.Estado,
            Autorizado = x.Autorizado,
            NumeroAutorizacion = x.NumeroAutorizacion,
            MensajeSri = x.MensajeSri,
            FechaAutorizacion = x.FechaAutorizacion,
            ClaveAcceso = x.ClaveAcceso,
            FechaVencimientoDocumento = x.FechaVencimientoDocumento,
            SaldoPendienteDocumento = Math.Max(x.SaldoPendienteDocumento, 0m),
            XmlUrl = !string.IsNullOrWhiteSpace(x.RucEmisor)
                ? ConstruirXmlUrl(x.NumeroNotaCredito, x.RucEmisor)
                : ""
        }).ToList();

        return resultado;
    }

    public async Task<List<NotaCreditoListDto>> ListarNotasCreditoBackOfficeAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var data = await (
            from nc in db.NotaCreditos.AsNoTracking()
            join c in db.Clientes.AsNoTracking()
                on nc.CodClientes equals c.Codcliente into cliJoin
            from c in cliJoin.DefaultIfEmpty()
            join f in db.Facturas.AsNoTracking()
                on nc.IdDocModificado equals f.Codfactura into facturaJoin
            from f in facturaJoin.DefaultIfEmpty()
            join e in db.Emisores.AsNoTracking()
                on nc.CodEmisor equals e.Codigo into emiJoin
            from e in emiJoin.DefaultIfEmpty()
            join ti in db.Identificacion.AsNoTracking()
                on c.Tipoidentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()
            where e != null && e.EsEmisorSistema
            orderby nc.Sec descending
            select new
            {
                Sec = nc.Sec,
                NumeroNotaCredito = nc.NumNotaCredito ?? "",
                Serie = nc.Serie ?? "",
                Cliente = c != null
                    ? (!string.IsNullOrWhiteSpace(c.Nombrerazonsocial)
                        ? c.Nombrerazonsocial
                        : ((c.Nombres ?? "") + " " + (c.Apellidos ?? "")).Trim())
                    : "",
                IdentificacionCliente = c != null ? (c.Numeroidentificacion ?? "") : "",
                TipoIdentificacionCliente = ti != null ? (ti.IdeDescripcion ?? "") : "",
                NumeroDocModificado = nc.NumDocModificado ?? "",
                FechaDocumentoModificado = nc.FechaEmiDocModificado,
                Subtotal = nc.Subtotal ?? 0m,
                Iva = nc.Iva ?? 0m,
                Total = nc.ValorTotal ?? 0m,
                Motivo = nc.Motivo ?? "",
                Estado = nc.Estado ?? false,
                Autorizado = nc.Autorizado ?? "",
                NumeroAutorizacion = nc.NumAutorizacion ?? "",
                MensajeSri = nc.Observacion ?? "",
                FechaAutorizacion = nc.FchAutorizacion,
                ClaveAcceso = nc.CodClave ?? "",
                RucEmisor = e.Ruc ?? "",
                FechaVencimientoDocumento = f != null
                    ? (f.Fechavence
                        ?? ((f.Fchautorizacion ?? f.Fechaentrega).HasValue
                            ? (c != null && c.DiasCredito.HasValue
                                ? (f.Fchautorizacion ?? f.Fechaentrega)!.Value.AddDays(c.DiasCredito.Value)
                                : (DateTime?)null)
                            : null))
                    : null,
                SaldoPendienteDocumento = f != null
                    ? ((f.Valortotal ?? 0m) - (db.Abonos
                        .Where(a => a.codFactura == f.Codfactura && a.estado == true)
                        .Sum(a => (decimal?)a.abono) ?? 0m))
                    : 0m
            })
            .ToListAsync();

        return data.Select(x => new NotaCreditoListDto
        {
            Sec = x.Sec,
            NumeroNotaCredito = x.NumeroNotaCredito,
            Serie = x.Serie,
            Cliente = x.Cliente,
            IdentificacionCliente = x.IdentificacionCliente,
            TipoIdentificacionCliente = x.TipoIdentificacionCliente,
            NumeroDocModificado = x.NumeroDocModificado,
            FechaDocumentoModificado = x.FechaDocumentoModificado,
            Subtotal = x.Subtotal,
            Iva = x.Iva,
            Total = x.Total,
            Motivo = x.Motivo,
            Estado = x.Estado,
            Autorizado = x.Autorizado,
            NumeroAutorizacion = x.NumeroAutorizacion,
            MensajeSri = x.MensajeSri,
            FechaAutorizacion = x.FechaAutorizacion,
            ClaveAcceso = x.ClaveAcceso,
            FechaVencimientoDocumento = x.FechaVencimientoDocumento,
            SaldoPendienteDocumento = Math.Max(x.SaldoPendienteDocumento, 0m),
            XmlUrl = !string.IsNullOrWhiteSpace(x.RucEmisor)
                ? ConstruirXmlUrl(x.NumeroNotaCredito, x.RucEmisor)
                : ""
        }).ToList();
    }
    public string GenerarClaveAcceso(DateTime fecha, string ruc, string ambiente, string serie, string secuencial, string tipoEmi)
    {
        string fechaStr = fecha.ToString("ddMMyyyy");
        string tipoComp = "04";
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
    public async Task<NotaCreditoDetalleViewDto?> GetNotaCreditoDetalleUsuarioAsync(int sec, int idUsuario)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var existe = await db.NotaCreditos.AsNoTracking().AnyAsync(x => x.Sec == sec && x.Usuario == idUsuario);
        if (!existe)
            return null;

        return await GetNotaCreditoDetalleAsync(sec);
    }

    public async Task<string?> AsegurarXmlNotaCreditoUsuarioAsync(int sec, int idUsuario)
    {
        var detalle = await GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario);
        if (detalle?.NotaCredito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaXml = ConstruirXmlPath(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "");
        if (!File.Exists(rutaXml))
        {
            try
            {
                rutaXml = await ProcesarXmlNotaCreditoAsync(sec);
            }
            catch (Exception)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
            return null;

        return ConstruirXmlUrl(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "");
    }

    public async Task<string?> AsegurarXmlNotaCreditoAsync(int sec)
    {
        var detalle = await GetNotaCreditoDetalleAsync(sec);
        if (detalle?.NotaCredito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaXml = ConstruirXmlPath(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "");
        if (!File.Exists(rutaXml))
        {
            try
            {
                rutaXml = await ProcesarXmlNotaCreditoAsync(sec);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return ConstruirXmlUrl(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "");
    }

    public async Task<string?> AsegurarXmlNotaCreditoRutaUsuarioAsync(int sec, int idUsuario)
    {
        var detalle = await GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario);
        if (detalle?.NotaCredito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaXml = ConstruirXmlPath(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "");
        if (File.Exists(rutaXml))
            return rutaXml;

        try
        {
            rutaXml = await ProcesarXmlNotaCreditoAsync(sec);
        }
        catch (Exception)
        {
            return null;
        }

        return File.Exists(rutaXml) ? rutaXml : null;
    }

    public async Task<string?> AsegurarPdfNotaCreditoUsuarioAsync(int sec, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        await AsegurarDatosAutorizacionNotaCreditoAsync(sec);

        var detalle = await GetNotaCreditoDetalleUsuarioAsync(sec, idUsuario);
        if (detalle?.NotaCredito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        string rutaPdf;
        try
        {
            rutaPdf = await _notaCreditoPdfService.GenerarPdfNotaCreditoAsync(detalle, formato);
        }
        catch (Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
            return null;

        return ConstruirPdfUrl(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "", formato);
    }

    public async Task<string?> AsegurarPdfNotaCreditoAsync(int sec, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        await AsegurarDatosAutorizacionNotaCreditoAsync(sec);

        var detalle = await GetNotaCreditoDetalleAsync(sec);
        if (detalle?.NotaCredito == null || string.IsNullOrWhiteSpace(detalle.Emisor?.Ruc))
            return null;

        var rutaPdf = ConstruirPdfPath(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "", formato);
        if (!File.Exists(rutaPdf))
        {
            try
            {
                rutaPdf = await _notaCreditoPdfService.GenerarPdfNotaCreditoAsync(detalle, formato);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return ConstruirPdfUrl(detalle.NotaCredito.NumNotaCredito ?? "", detalle.Emisor.Ruc ?? "", formato);
    }

    public async Task<NotaCreditoDetalleViewDto?> GetNotaCreditoDetalleAsync(int sec)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var rawData = await (
            from nc in db.NotaCreditos.AsNoTracking()
            join c in db.Clientes.AsNoTracking()
                on nc.CodClientes equals c.Codcliente into cliJoin
            from c in cliJoin.DefaultIfEmpty()

            join e in db.Emisores.AsNoTracking()
                on nc.CodEmisor equals e.Codigo into emiJoin
            from e in emiJoin.DefaultIfEmpty()

            join ti in db.Identificacion.AsNoTracking()
                on c.Tipoidentificacion equals ti.IdeCodigo into tipoJoin
            from ti in tipoJoin.DefaultIfEmpty()

            where nc.Sec == sec
            select new
            {
                NotaCredito = nc,
                Cliente = c,
                Emisor = e,
                TipoIdentificacionCliente = ti != null ? (ti.IdeDescripcion ?? "") : ""
            })
            .FirstOrDefaultAsync();

        if (rawData == null)
            return null;

        var detalles = await db.DetallesNotaCredito
            .AsNoTracking()
            .Where(d => d.CodNotaCredito == sec)
            .OrderBy(d => d.CodLinea)
            .Select(d => new NotaCreditoDetalleLineaDto
            {
                CodigoInterno = string.IsNullOrWhiteSpace(d.CodPrincipal) ? d.CodProducto.ToString() : d.CodPrincipal,
                Descripcion = d.DescripProducto ?? "",
                Cantidad = d.CantProducto ?? 0m,
                PrecioUnitario = d.PrecioProducto ?? 0m,
                Descuento = d.Descuento ?? 0m,
                Subtotal = d.ValorTProducto ?? 0m,
                TarifaIva = d.Tarifa ?? d.PorImp ?? 0,
                ValorIva = d.ValorIVA ?? 0m,
                Total = (d.ValorTProducto ?? 0m) + (d.ValorIVA ?? 0m)
            })
            .ToListAsync();

        return new NotaCreditoDetalleViewDto
        {
            NotaCredito = rawData.NotaCredito,
            Cliente = rawData.Cliente,
            Emisor = rawData.Emisor,
            TipoIdentificacionCliente = rawData.TipoIdentificacionCliente,
            NumeroCompleto = FormatearNumeroCompleto(rawData.NotaCredito.Serie, rawData.NotaCredito.NumNotaCredito),
            NumeroDocModificadoVisual = FormatearDocModificado(rawData.NotaCredito.Serie, rawData.NotaCredito.NumDocModificado),
            XmlUrl = !string.IsNullOrWhiteSpace(rawData.Emisor?.Ruc)
                ? ConstruirXmlUrl(rawData.NotaCredito.NumNotaCredito ?? "", rawData.Emisor.Ruc ?? "")
                : "",
            Detalles = detalles
        };
    }

    public async Task<string> ProcesarXmlNotaCreditoAsync(int sec)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var notaCredito = await db.NotaCreditos.FirstOrDefaultAsync(x => x.Sec == sec);
        if (notaCredito == null)
            throw new InvalidOperationException("No se encontró la nota de crédito para generar el XML.");

        await AsegurarClaveAccesoNotaCreditoAsync(db, notaCredito);
        ValidarDatosAutorizacionNotaCredito(notaCredito);

        var detalles = await db.DetallesNotaCredito
            .AsNoTracking()
            .Where(d => d.CodNotaCredito == sec)
            .OrderBy(d => d.CodLinea)
            .Select(d => new DetalleNcDto
            {
                Codproducto = d.CodProducto,
                Codprincipal = d.CodPrincipal,
                Codauxiliar = d.CodAuxiliar,
                Descripcion = d.DescripProducto ?? "",
                Detalle = d.DescripProducto ?? "",
                Cantidad = d.CantProducto ?? 0m,
                Preciounitario = d.PrecioProducto ?? 0m,
                Descuento = d.Descuento ?? 0m,
                Subtotal = d.ValorTProducto ?? 0m,
                Iva = d.Tarifa ?? d.PorImp ?? 0,
                Total = (d.ValorTProducto ?? 0m) + (d.ValorIVA ?? 0m)
            })
            .ToListAsync();

        var emisor = await db.Emisores.AsNoTracking().FirstOrDefaultAsync(e => e.Codigo == notaCredito.CodEmisor);
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Codcliente == notaCredito.CodClientes);

        var xmlContent = GenerarXmlNotaCredito(notaCredito, detalles, emisor, cliente);
        await GuardarXmlEnServidor(xmlContent, notaCredito.NumNotaCredito ?? "", emisor?.Ruc ?? "");

        return ConstruirXmlPath(notaCredito.NumNotaCredito ?? "", emisor?.Ruc ?? "");
    }

    public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarNotaCreditoPorCorreoAsync(int sec, string? rutaXmlExistente = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var notaCredito = await db.NotaCreditos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sec == sec);

        if (notaCredito == null)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "No se encontró la nota de crédito para el envío por correo."
            };
        }

        var cliente = notaCredito.CodClientes.HasValue
            ? await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Codcliente == notaCredito.CodClientes.Value)
            : null;

        var metadata = LeerNotaCreditoCorreoMetadata(notaCredito.Detalleextra);
        var destinatariosBase = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            db,
            notaCredito.Usuario,
            notaCredito.CodClientes,
            cliente?.Correo);

        var destinatarios = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(
            metadata.Destinatarios
                .Concat(ParseCorreosDocumento(notaCredito.Correoad))
                .Concat(destinatariosBase));

        if (metadata.CorreoEnviado)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                YaEnviado = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = destinatarios.Any()
                    ? $"El correo de esta nota de crédito ya fue enviado anteriormente a {destinatarios.Count} destinatario(s)."
                    : "El correo de esta nota de crédito ya fue enviado anteriormente."
            };
        }

        if (!destinatarios.Any())
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                SinDestinatarios = true,
                Mensaje = "La nota de crédito no tiene correos configurados para el envío."
            };
        }

        if (!NotaCreditoEstaAutorizada(notaCredito.Autorizado))
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                PendienteAutorizacion = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La nota de crédito aún no está autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s) hasta que Autorizado tenga un valor aprobado."
            };
        }

        var rutaXml = rutaXmlExistente;
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            try
            {
                rutaXml = await ProcesarXmlNotaCreditoAsync(sec);
            }
            catch (Exception ex)
            {
                return await ActualizarEstadoCorreoNotaAsync(
                    sec,
                    destinatarios,
                    false,
                    ex.Message,
                    new FacturaCorreoEnvioResultadoDto
                    {
                        Error = true,
                        TotalDestinatarios = destinatarios.Count,
                        Mensaje = $"No se pudo generar el XML de la nota de crÃ©dito para el correo: {ex.Message}"
                    });
            }
        }

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            return await ActualizarEstadoCorreoNotaAsync(
                sec,
                destinatarios,
                false,
                "No se pudo generar o ubicar el XML adjunto para el correo.",
                new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = "No se pudo generar o ubicar el XML adjunto para enviar la nota de crédito por correo."
                });
        }

        try
        {
            var notaView = await GetNotaCreditoDetalleAsync(sec)
                ?? throw new InvalidOperationException("No se pudo cargar el detalle de la nota de crédito para generar el PDF adjunto.");

            var rutaPdf = await _notaCreditoPdfService.GenerarPdfNotaCreditoAsync(notaView);
            if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
                throw new FileNotFoundException("No se pudo generar o ubicar el PDF adjunto para el correo.", rutaPdf);

            await _emailService.EnviarNotaCreditoAsync(
                ObtenerNumeroNotaDocumento(notaView.NotaCredito),
                notaView.NumeroDocModificadoVisual,
                destinatarios,
                ObtenerNombreCliente(cliente),
                notaView.NotaCredito.ValorTotal,
                rutaXml,
                rutaPdf);

            return await ActualizarEstadoCorreoNotaAsync(
                sec,
                destinatarios,
                true,
                null,
                new FacturaCorreoEnvioResultadoDto
                {
                    Enviado = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"Nota de crédito enviada correctamente por correo a {destinatarios.Count} destinatario(s)."
                });
        }
        catch (Exception ex)
        {
            return await ActualizarEstadoCorreoNotaAsync(
                sec,
                destinatarios,
                false,
                ex.Message,
                new FacturaCorreoEnvioResultadoDto
                {
                    Error = true,
                    TotalDestinatarios = destinatarios.Count,
                    Mensaje = $"No se pudo enviar la nota de crédito por correo: {ex.Message}"
                });
        }
    }

    public async Task<List<int>> GetNotasCreditoAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var limite = Math.Max(1, maxRegistros);
        var limiteCandidatas = Math.Max(limite * 5, 50);

        var candidatas = await db.NotaCreditos
            .AsNoTracking()
            .Where(n =>
                n.Estado == true &&
                !string.IsNullOrWhiteSpace(n.Autorizado) &&
                (!string.IsNullOrWhiteSpace(n.Correoad) || !string.IsNullOrWhiteSpace(n.Detalleextra)))
            .OrderBy(n => n.Sec)
            .Select(n => new
            {
                n.Sec,
                n.Autorizado,
                n.Detalleextra
            })
            .Take(limiteCandidatas)
            .ToListAsync();

        return candidatas
            .Where(n => NotaCreditoEstaAutorizada(n.Autorizado) && !LeerNotaCreditoCorreoMetadata(n.Detalleextra).CorreoEnviado)
            .Take(limite)
            .Select(n => n.Sec)
            .ToList();
    }

    private async Task<FacturaCorreoEnvioResultadoDto> ActualizarEstadoCorreoNotaAsync(
        int sec,
        List<string> destinatarios,
        bool enviado,
        string? ultimoError,
        FacturaCorreoEnvioResultadoDto resultado)
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        var notaCredito = await db.NotaCreditos.FirstOrDefaultAsync(x => x.Sec == sec);
        if (notaCredito == null)
            return resultado;

        var metadata = LeerNotaCreditoCorreoMetadata(notaCredito.Detalleextra);
        metadata.Destinatarios = NormalizarCorreos(destinatarios);
        metadata.CorreoEnviado = enviado;
        metadata.FechaEnvioCorreo = enviado ? DateTime.Now : metadata.FechaEnvioCorreo;
        metadata.UltimoErrorCorreo = ultimoError;

        notaCredito.Correoad = SerializarCorreosDocumento(destinatarios);
        notaCredito.Detalleextra = EscribirNotaCreditoCorreoMetadata(metadata);
        await db.SaveChangesAsync();

        return resultado;
    }

    private string ConstruirXmlUrl(string numNotaCredito, string ruc)
    {
        var fileName = $"{ruc}_04_{(numNotaCredito ?? "").Replace("-", "").PadLeft(9, '0')}.xml";
        return $"/notas_de_credito/{fileName}";
    }

    private string ConstruirXmlPath(string numNotaCredito, string ruc)
    {
        var folderPath = Path.Combine(_env.WebRootPath, "notas_de_credito");
        var fileName = $"{ruc}_04_{(numNotaCredito ?? "").Replace("-", "").PadLeft(9, '0')}.xml";
        return Path.Combine(folderPath, fileName);
    }

    private string ConstruirPdfUrl(string numNotaCredito, string ruc, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var fileName = $"{ruc}_04_{(numNotaCredito ?? "").Replace("-", "").PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";
        return $"/notas_de_credito/{fileName}";
    }

    private string ConstruirPdfPath(string numNotaCredito, string ruc, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var folderPath = Path.Combine(_env.WebRootPath, "notas_de_credito");
        var fileName = $"{ruc}_04_{(numNotaCredito ?? "").Replace("-", "").PadLeft(9, '0')}{formato.ObtenerSufijoArchivo()}.pdf";
        return Path.Combine(folderPath, fileName);
    }

    private static string FormatearNumeroCompleto(string? serie, string? secuencial)
    {
        var s = (serie ?? "").Replace("-", "").Trim();
        var n = (secuencial ?? "").Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private static void ValidarDatosAutorizacionNotaCredito(NotaCredito notaCredito)
    {
        if (!NotaCreditoEstaAutorizada(notaCredito.Autorizado))
            return;

        if (string.IsNullOrWhiteSpace(notaCredito.CodClave) ||
            string.IsNullOrWhiteSpace(notaCredito.NumAutorizacion) ||
            !notaCredito.FchAutorizacion.HasValue)
        {
            throw new InvalidOperationException(
                "La nota de crédito está autorizada pero no tiene completos los datos de autorización (clave de acceso, número o fecha).");
        }
    }

    private async Task AsegurarDatosAutorizacionNotaCreditoAsync(int sec)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var notaCredito = await db.NotaCreditos.FirstOrDefaultAsync(x => x.Sec == sec);
        if (notaCredito == null)
            return;

        await AsegurarClaveAccesoNotaCreditoAsync(db, notaCredito);
    }

    private async Task AsegurarClaveAccesoNotaCreditoAsync(AppDbContext db, NotaCredito notaCredito)
    {
        var requiereGuardar = false;
        var fechaReferencia = ObtenerFechaReferenciaClaveNotaCredito(notaCredito);

        if (string.IsNullOrWhiteSpace(notaCredito.CodClave))
        {
            var emisorRuc = await db.Emisores
                .AsNoTracking()
                .Where(e => e.Codigo == notaCredito.CodEmisor)
                .Select(e => e.Ruc)
                .FirstOrDefaultAsync();

            var serieLimpia = (notaCredito.Serie ?? "001001").Replace("-", "").Trim();
            var secuencial = (notaCredito.NumNotaCredito ?? notaCredito.Sec.ToString(CultureInfo.InvariantCulture)).Trim().PadLeft(9, '0');
            notaCredito.CodClave = ObtenerClaveAccesoNotaCredito(
                notaCredito,
                emisorRuc,
                "2",
                serieLimpia,
                secuencial,
                fechaReferencia);
            requiereGuardar = !string.IsNullOrWhiteSpace(notaCredito.CodClave);
        }

        if (NotaCreditoEstaAutorizada(notaCredito.Autorizado))
        {
            if (string.IsNullOrWhiteSpace(notaCredito.NumAutorizacion) && !string.IsNullOrWhiteSpace(notaCredito.CodClave))
            {
                notaCredito.NumAutorizacion = notaCredito.CodClave;
                requiereGuardar = true;
            }

            if (!notaCredito.FchAutorizacion.HasValue)
            {
                notaCredito.FchAutorizacion = fechaReferencia;
                requiereGuardar = true;
            }
        }

        if (requiereGuardar)
            await db.SaveChangesAsync();
    }

    private string ObtenerClaveAccesoNotaCredito(
        NotaCredito notaCredito,
        string? ruc,
        string ambiente,
        string serieLimpia,
        string secuencial,
        DateTime? fechaReferencia = null)
    {
        if (!string.IsNullOrWhiteSpace(notaCredito.CodClave))
            return notaCredito.CodClave.Trim();

        var fecha = fechaReferencia ?? ObtenerFechaReferenciaClaveNotaCredito(notaCredito);
        return GenerarClaveAcceso(fecha, ruc ?? string.Empty, ambiente, serieLimpia, secuencial, "1");
    }

    private static DateTime ObtenerFechaReferenciaClaveNotaCredito(NotaCredito notaCredito)
    {
        if (notaCredito.FchAutorizacion.HasValue)
            return notaCredito.FchAutorizacion.Value;

        if (!string.IsNullOrWhiteSpace(notaCredito.FechaAutoSri) &&
            DateTime.TryParse(notaCredito.FechaAutoSri, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var fechaSri))
        {
            return fechaSri;
        }

        return DateTime.Now;
    }

    private static string FormatearDocModificado(string? serie, string? numDoc)
    {
        var completo = FormatearNumeroDocumentoCompleto(numDoc);
        if (!string.IsNullOrWhiteSpace(completo))
            return completo;

        var s = (serie ?? "").Replace("-", "").Trim();
        var n = (numDoc ?? "").Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private static string FormatearDocumentoModificadoXml(string? serie, string? numDoc)
    {
        var completo = FormatearNumeroDocumentoCompleto(numDoc);
        if (!string.IsNullOrWhiteSpace(completo))
            return completo;

        var s = (serie ?? "").Replace("-", "").Trim();
        var n = (numDoc ?? "").Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }

    private static string? FormatearNumeroDocumentoCompleto(string? numDoc)
    {
        var limpio = new string((numDoc ?? string.Empty).Where(char.IsDigit).ToArray());
        if (limpio.Length != 15)
            return null;

        return $"{limpio[..3]}-{limpio.Substring(3, 3)}-{limpio.Substring(6, 9)}";
    }

    private static string ObtenerCodigoPorcentajeSri(int iva)
    {
        return iva switch
        {
            0 => "0",
            5 => "5",
            8 => "8",
            15 => "4",
            _ => "2"
        };
    }

    private List<NotaCreditoDetalleLineaDto> LeerDetallesDesdeXml(string xmlPath)
    {
        var doc = XDocument.Load(xmlPath);

        var detalles = doc.Descendants()
            .Where(x => x.Name.LocalName == "detalle")
            .Select(d =>
            {
                var impuesto = d.Descendants().FirstOrDefault(x => x.Name.LocalName == "impuesto");

                decimal subtotal = ParseDecimal(d.Elements().FirstOrDefault(x => x.Name.LocalName == "precioTotalSinImpuesto")?.Value);
                decimal valorIva = ParseDecimal(impuesto?.Elements().FirstOrDefault(x => x.Name.LocalName == "valor")?.Value);
                decimal tarifa = ParseDecimal(impuesto?.Elements().FirstOrDefault(x => x.Name.LocalName == "tarifa")?.Value);

                return new NotaCreditoDetalleLineaDto
                {
                    CodigoInterno = d.Elements().FirstOrDefault(x => x.Name.LocalName == "codigoInterno")?.Value ?? "",
                    Descripcion = d.Elements().FirstOrDefault(x => x.Name.LocalName == "descripcion")?.Value ?? "",
                    Cantidad = ParseDecimal(d.Elements().FirstOrDefault(x => x.Name.LocalName == "cantidad")?.Value),
                    PrecioUnitario = ParseDecimal(d.Elements().FirstOrDefault(x => x.Name.LocalName == "precioUnitario")?.Value),
                    Descuento = ParseDecimal(d.Elements().FirstOrDefault(x => x.Name.LocalName == "descuento")?.Value),
                    Subtotal = subtotal,
                    TarifaIva = tarifa,
                    ValorIva = valorIva,
                    Total = subtotal + valorIva
                };
            })
            .ToList();

        return detalles;
    }

    private decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        value = value.Trim().Replace(",", ".");

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0m;
    }

    public async Task<mensajeSRI> EnviarXmlAApiFrameworkAsync(string rutaArchivoxml, string rutaFirma, string password)
    {
        try
        {
            if (!File.Exists(rutaArchivoxml))
                throw new FileNotFoundException($"No se encontró el archivo XML en la ruta: {rutaArchivoxml}");
            return await _sriXmlProcessorService.ProcessXmlAsync(rutaArchivoxml, rutaFirma, password).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new mensajeSRI
            {
                xml = $"<error><tipo>ExcepcionBlazor</tipo><mensaje>{ex.Message}</mensaje></error>",
                mensaje = ex.Message
            };
        }
    }

    public async Task<mensajeSRI> EnviarXmlAApiFrameworkConReintentosAsync(
        string rutaArchivoxml,
        string rutaFirma,
        string password,
        int maxIntentos = 3,
        int esperaEntreIntentosMs = 1800)
    {
        mensajeSRI ultimoResultado = new();

        for (var intento = 1; intento <= Math.Max(1, maxIntentos); intento++)
        {
            ultimoResultado = await EnviarXmlAApiFrameworkAsync(rutaArchivoxml, rutaFirma, password);
            if (!string.IsNullOrWhiteSpace(ultimoResultado.autorizacion))
                return ultimoResultado;

            if (intento < maxIntentos)
                await Task.Delay(esperaEntreIntentosMs);
        }

        return ultimoResultado;
    }

    public async Task<EmisionSriNotaCreditoResultado> EmitirNotaCreditoSriAsync(
        int sec,
        int? idUsuario = null,
        bool intentarEnviarCorreo = true)
    {
        if (sec <= 0)
        {
            return new EmisionSriNotaCreditoResultado
            {
                Sec = sec,
                EstadoSri = "ERROR INTERNO",
                Message = "No se pudo identificar la nota de crédito a enviar al SRI."
            };
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        var nota = await context.NotaCreditos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Sec == sec && (!idUsuario.HasValue || x.Usuario == idUsuario.Value));

        if (nota == null)
        {
            return new EmisionSriNotaCreditoResultado
            {
                Sec = sec,
                EstadoSri = "ERROR INTERNO",
                Message = "No se encontró la nota de crédito o no tienes permisos para enviarla."
            };
        }

        if (DocumentoAutorizacionHelper.EstaAutorizado(nota.Autorizado))
        {
            return new EmisionSriNotaCreditoResultado
            {
                Success = true,
                Autorizada = true,
                Sec = sec,
                EstadoSri = DocumentoAutorizacionHelper.EstadoAutorizado,
                Message = $"La nota de crédito {ObtenerNumeroNotaDocumento(nota)} ya está autorizada por el SRI."
            };
        }

        var emisor = await context.Emisores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo == nota.CodEmisor);

        if (emisor == null)
        {
            await ActualizarAutorizacionNCAsync(sec, string.Empty, DateTime.Now.ToString("O"), "No se encontró el emisor de la nota de crédito.", "ERROR INTERNO");
            return new EmisionSriNotaCreditoResultado
            {
                Sec = sec,
                EstadoSri = "ERROR INTERNO",
                Message = "No se encontró el emisor asociado a la nota de crédito."
            };
        }

        var rutaCertificado = ResolverRutaCertificado(emisor);
        var claveCertificado = ResolverClaveCertificado(emisor);
        if (string.IsNullOrWhiteSpace(rutaCertificado) || !File.Exists(rutaCertificado) || string.IsNullOrWhiteSpace(claveCertificado))
        {
            await ActualizarAutorizacionNCAsync(sec, string.Empty, DateTime.Now.ToString("O"), "El emisor no tiene configurada una firma electrónica válida para emitir la nota de crédito.", "ERROR INTERNO");
            return new EmisionSriNotaCreditoResultado
            {
                Sec = sec,
                EstadoSri = "ERROR INTERNO",
                Message = "El emisor no tiene configurado el certificado electrónico requerido para el envío al SRI."
            };
        }

        var rutaXml = idUsuario.HasValue
            ? await AsegurarXmlNotaCreditoRutaUsuarioAsync(sec, idUsuario.Value)
            : await ProcesarXmlNotaCreditoAsync(sec);

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            await ActualizarAutorizacionNCAsync(sec, string.Empty, DateTime.Now.ToString("O"), "No se pudo generar el XML de la nota de crédito para el envío al SRI.", "ERROR INTERNO");
            return new EmisionSriNotaCreditoResultado
            {
                Sec = sec,
                EstadoSri = "ERROR INTERNO",
                Message = "No se pudo generar el XML de la nota de crédito para enviarla al SRI."
            };
        }

        var respuestaSri = await EnviarXmlAApiFrameworkConReintentosAsync(
            rutaXml,
            rutaCertificado,
            claveCertificado);

        var fechaRespuesta = string.IsNullOrWhiteSpace(respuestaSri.fecha)
            ? DateTime.Now.ToString("O")
            : respuestaSri.fecha;

        if (string.Equals(respuestaSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(respuestaSri.autorizacion))
        {
            await ActualizarAutorizacionNCAsync(
                sec,
                respuestaSri.autorizacion ?? string.Empty,
                fechaRespuesta,
                "ok",
                DocumentoAutorizacionHelper.EstadoAutorizado);

            FacturaCorreoEnvioResultadoDto? resultadoCorreo = null;
            if (intentarEnviarCorreo)
                resultadoCorreo = await IntentarEnviarNotaCreditoPorCorreoAsync(sec, rutaXml);

            var mensajeOk = $"La nota de crédito {ObtenerNumeroNotaDocumento(nota)} fue autorizada correctamente por el SRI.";
            if (!string.IsNullOrWhiteSpace(resultadoCorreo?.Mensaje))
                mensajeOk = $"{mensajeOk} {resultadoCorreo.Mensaje}";

            return new EmisionSriNotaCreditoResultado
            {
                Success = true,
                Autorizada = true,
                Sec = sec,
                EstadoSri = DocumentoAutorizacionHelper.EstadoAutorizado,
                Message = mensajeOk,
                RutaXml = rutaXml,
                RespuestaSri = respuestaSri,
                ResultadoCorreo = resultadoCorreo
            };
        }

        var mensajeErrorSri = ExtraerMensajeSri(respuestaSri);
        var estadoRespuesta = string.IsNullOrWhiteSpace(respuestaSri.estado)
            ? DocumentoAutorizacionHelper.EstadoNoAutorizado
            : respuestaSri.estado.Trim();
        var estadoPersistencia = string.Equals(estadoRespuesta, DocumentoAutorizacionHelper.EstadoNoAutorizado, StringComparison.OrdinalIgnoreCase)
            ? "ERROR SRI"
            : "ERROR INTERNO";

        await ActualizarAutorizacionNCAsync(
            sec,
            respuestaSri.autorizacion ?? string.Empty,
            fechaRespuesta,
            mensajeErrorSri,
            estadoPersistencia);

        return new EmisionSriNotaCreditoResultado
        {
            Success = true,
            Autorizada = false,
            Sec = sec,
            EstadoSri = estadoRespuesta,
            Message = $"La nota de crédito {ObtenerNumeroNotaDocumento(nota)} se guardó, pero el SRI devolvió observaciones: {mensajeErrorSri}",
            RutaXml = rutaXml,
            RespuestaSri = respuestaSri
        };
    }

    public async Task ActualizarAutorizacionNCAsync(int codFactura, string numeroAutorizacion, string fechaAutorizacion, string mensaje, string autorizado)
    {
        // Buscamos la factura en la base de datos por su código único
        await using var context = await _dbFactory.CreateDbContextAsync();
        var NCDb = await context.NotaCreditos.FirstOrDefaultAsync(f => f.Sec == codFactura);

        if (NCDb != null)
        {
            // Actualizamos los campos correspondientes
            NCDb.NumAutorizacion = numeroAutorizacion;
            NCDb.FechaAutoSri = fechaAutorizacion;
            NCDb.FchAutorizacion = DateTime.Now;
            NCDb.Autorizado = NormalizarEstadoAutorizacionNc(autorizado);
            NCDb.Observacion = mensaje;
            await AsegurarClaveAccesoNotaCreditoAsync(context, NCDb);

            // Guardamos los cambios en la base de datos
            await context.SaveChangesAsync();

            if (NCDb.Usuario is > 0)
            {
                try
                {
                    await AsegurarPdfNotaCreditoUsuarioAsync(codFactura, NCDb.Usuario.Value);
                }
                catch
                {
                }
            }
        }
        else
        {
            throw new Exception($"No se encontró la factura con el código {codFactura} para actualizar la autorización.");
        }
    }

    private static string NormalizarEstadoAutorizacionNc(string? autorizado)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return string.Empty;

        var valor = autorizado.Trim();

        if (valor.Equals("AUTORIZADO", StringComparison.OrdinalIgnoreCase))
            return "AUTORIZADO";

        if (valor.StartsWith("ERROR INTERNO", StringComparison.OrdinalIgnoreCase))
            return "ERROR INTERNO";

        if (valor.StartsWith("ERROR SRI", StringComparison.OrdinalIgnoreCase))
            return "ERROR SRI";

        return valor.Length <= 20 ? valor : valor[..20];
    }

    public async Task<bool> AnularNotaCreditoDirectoAsync(int sec)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        try
        {
            // Buscamos el registro en la tabla de notas de crédito utilizando el campo 'Sec'
            var nota = await context.NotaCreditos.FirstOrDefaultAsync(n => n.Sec == sec);
            if (nota != null)
            {
                nota.Estado = false; // Seteamos a false
                await context.SaveChangesAsync();
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
    public class DetalleNcDto
    {
        public int Codproducto { get; set; }
        public string? Codprincipal { get; set; }
        public string? Codauxiliar { get; set; }
        public string Descripcion { get; set; } = "";
        public string? Detalle { get; set; }
        public decimal Cantidad { get; set; }
        public decimal Preciounitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal PorcentajeDescuento { get; set; }
        public decimal Subtotal { get; set; }
        public int Iva { get; set; }
        public decimal Total { get; set; }
    }

    private static async Task<List<DetalleNcDto>> ConstruirDetallesAutomaticosDisponiblesAsync(AppDbContext db, int codFactura)
    {
        var detallesFactura = await db.Detallefacturas
            .AsNoTracking()
            .Where(d => d.Codfactura == codFactura)
            .OrderBy(d => d.Codlinea)
            .ToListAsync();

        var anulados = await (
            from nc in db.NotaCreditos.AsNoTracking()
            join dnc in db.DetallesNotaCredito.AsNoTracking() on nc.Sec equals dnc.CodNotaCredito
            where nc.IdDocModificado == codFactura && nc.Estado == true
            select dnc
        ).ToListAsync();

        return detallesFactura
            .Select(df =>
            {
                var anuladosProducto = anulados.Where(a => a.CodProducto == df.Codproducto).ToList();
                var cantidadRestante = df.Cantproducto - anuladosProducto.Sum(a => a.CantProducto ?? 0m);
                var descuentoRestante = (df.Descuento ?? 0m) - anuladosProducto.Sum(a => a.Descuento ?? 0m);
                var subtotalRestante = df.Valortproducto - anuladosProducto.Sum(a => a.ValorTProducto ?? 0m);

                return new DetalleNcDto
                {
                    Codproducto = df.Codproducto,
                    Codprincipal = df.Codprincipal,
                    Codauxiliar = df.Codauxiliar,
                    Descripcion = df.Descripproducto ?? string.Empty,
                    Detalle = df.Descripproducto ?? string.Empty,
                    Cantidad = cantidadRestante,
                    Preciounitario = df.Precioproducto,
                    Descuento = Math.Max(descuentoRestante, 0m),
                    PorcentajeDescuento = 0m,
                    Subtotal = Math.Max(subtotalRestante, 0m),
                    Iva = df.Tarifa,
                    Total = 0m
                };
            })
            .Where(d => d.Cantidad > 0m && d.Subtotal > 0m)
            .ToList();
    }

    private static string ExtraerMensajeSri(mensajeSRI respuestaSri)
    {
        if (!string.IsNullOrWhiteSpace(respuestaSri.mensaje))
            return respuestaSri.mensaje.Trim();

        if (!string.IsNullOrWhiteSpace(respuestaSri.estado))
            return respuestaSri.estado.Trim();

        if (!string.IsNullOrWhiteSpace(respuestaSri.estadoEnvio))
            return respuestaSri.estadoEnvio.Trim();

        if (!string.IsNullOrWhiteSpace(respuestaSri.xml))
            return respuestaSri.xml.Trim();

        return "No se recibió autorización del SRI.";
    }

    private static async Task<string> ObtenerSiguienteSecuencialNotaCreditoAsync(AppDbContext db, int idUsuario, string serieNcRaw, int? codEmisor = null)
    {
        var usuario = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == idUsuario)
            .Select(u => new
            {
                u.IdUsuario,
                u.idJefe,
                u.estadoAsociado
            })
            .FirstOrDefaultAsync();

        var titularId = usuario?.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario;

        var usuariosCuenta = await db.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();

        if (usuariosCuenta.Count == 0)
            usuariosCuenta.Add(idUsuario);

        var query = db.NotaCreditos
            .AsNoTracking()
            .Where(n =>
                n.Usuario.HasValue &&
                usuariosCuenta.Contains(n.Usuario.Value) &&
                n.Serie != null &&
                n.Serie.Replace("-", "") == serieNcRaw);

        if (codEmisor is > 0)
            query = query.Where(n => n.CodEmisor == codEmisor.Value);

        var lista = await query
            .Select(n => n.NumNotaCredito)
            .ToListAsync();

        var maximo = 0;
        foreach (var secuencial in lista)
        {
            if (string.IsNullOrWhiteSpace(secuencial))
                continue;

            if (int.TryParse(secuencial.Trim(), out var numero) && numero > maximo)
                maximo = numero;
        }

        return (maximo + 1).ToString("000000000", CultureInfo.InvariantCulture);
    }

    private async Task<(string SerieRaw, InitialSequencePromptState Estado, string Error)> ResolverSecuenciaAutomaticaNotaCreditoAsync(int idUsuario, Emisor emisor)
    {
        if (emisor.EsEmisorSistema)
        {
            var secuenciaSistema = await _emisorSistemaService.GetSecuenciaNotaCreditoSistemaAsync();
            if (secuenciaSistema == null || string.IsNullOrWhiteSpace(secuenciaSistema.SerieRaw))
            {
                return (string.Empty, new InitialSequencePromptState(), "Configura la serie maestra de notas de crédito en el emisor maestro antes de emitir desde backoffice.");
            }

            var estadoSistema = await _initialSequencePromptService.GetStateAsync(
                idUsuario,
                "nota-credito",
                secuenciaSistema.SerieRaw,
                emisor.Codigo);

            return (secuenciaSistema.SerieRaw, estadoSistema, string.Empty);
        }

        var resolucionNc = await ResolverSerieNotaCreditoAsync(idUsuario);
        var estado = await _initialSequencePromptService.GetStateAsync(
            idUsuario,
            "nota-credito",
            resolucionNc.SerieRaw,
            emisor.Codigo);

        return (resolucionNc.SerieRaw, estado, string.Empty);
    }

    private string ResolverClaveCertificado(Emisor? emisor)
    {
        var clave = _certificadoProtector.DesprotegerClave(emisor?.ClaveCertificado);
        return string.IsNullOrWhiteSpace(clave)
            ? emisor?.ClaveCertificado?.Trim() ?? string.Empty
            : clave.Trim();
    }

    private string ResolverRutaCertificado(Emisor? emisor)
    {
        var rutaOriginal = emisor?.PathCertificado;
        if (string.IsNullOrWhiteSpace(rutaOriginal))
            return string.Empty;

        if (Path.IsPathRooted(rutaOriginal) && File.Exists(rutaOriginal))
            return rutaOriginal;

        var normalizada = rutaOriginal.Trim().TrimStart('~', '/', '\\').Replace('\\', '/');
        if (normalizada.StartsWith("App_Data/", StringComparison.OrdinalIgnoreCase))
            normalizada = normalizada["App_Data/".Length..];

        var relativaSistema = normalizada.Replace('/', Path.DirectorySeparatorChar);
        var candidatos = new[]
        {
            Path.Combine(_env.ContentRootPath, relativaSistema),
            Path.Combine(_env.ContentRootPath, "App_Data", relativaSistema),
            Path.Combine(_env.ContentRootPath, "App_Data", "certs", "path", Path.GetFileName(normalizada))
        };

        return candidatos.FirstOrDefault(File.Exists) ?? rutaOriginal.Trim();
    }

    private static string? NormalizarSerieDocumento(string? serie)
    {
        if (string.IsNullOrWhiteSpace(serie))
            return null;

        var digitos = new string(serie.Where(char.IsDigit).ToArray());
        return digitos.Length >= 6 ? digitos[..6] : null;
    }
}
