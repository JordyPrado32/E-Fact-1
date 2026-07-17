using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System.Globalization;

namespace Simetric.Services;

public sealed class CompraDocumentosFacturacionService
{
    private const string MarcadorCompraNotas = "[COMPRA_DOCS:";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FacturacionService _facturacionService;
    private readonly EmisorSistemaService _emisorSistemaService;
    private readonly ILogger<CompraDocumentosFacturacionService> _logger;

    public CompraDocumentosFacturacionService(
        IDbContextFactory<AppDbContext> dbFactory,
        FacturacionService facturacionService,
        EmisorSistemaService emisorSistemaService,
        ILogger<CompraDocumentosFacturacionService> logger)
    {
        _dbFactory = dbFactory;
        _facturacionService = facturacionService;
        _emisorSistemaService = emisorSistemaService;
        _logger = logger;
    }

    public async Task<CompraDocumentosFacturaResultado> EmitirFacturaAsync(
        int idUsuario,
        CompraDocumentosHistorialItem compra,
        string? reference,
        string? authorizationCode)
    {
        if (idUsuario <= 0)
            return CompraDocumentosFacturaResultado.Error("No se pudo identificar al usuario titular de la compra.");

        if (compra == null || string.IsNullOrWhiteSpace(compra.Id))
            return CompraDocumentosFacturaResultado.Error("La compra aprobada no contiene un identificador válido.");

        await using var context = await _dbFactory.CreateDbContextAsync();

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.IdUsuario == idUsuario);

        if (usuario == null)
            return CompraDocumentosFacturaResultado.Error("No se encontró el usuario titular para emitir la factura.");

        var secuenciaSistema = await _emisorSistemaService.GetSecuenciaFacturaSistemaAsync();
        if (secuenciaSistema == null)
            return CompraDocumentosFacturaResultado.Error("No existe un emisor maestro global activo para facturar las recargas.");

        if (secuenciaSistema.OwnerUserId <= 0)
            return CompraDocumentosFacturaResultado.Error("El emisor maestro global no tiene una cuenta propietaria valida.");

        var emisorOwnerId = secuenciaSistema.OwnerUserId;
        if (!secuenciaSistema.Inicializada || string.IsNullOrWhiteSpace(secuenciaSistema.SerieRaw))
        {
            return CompraDocumentosFacturaResultado.Error(
                "Debes configurar primero la secuencia inicial del facturador del sistema en BackOffice > Emisor Maestro para evitar duplicidad de facturas.");
        }

        var marker = ConstruirMarcadorCompra(compra.Id);

        Factura? facturaExistente = null;
        if (compra.CodFactura is > 0)
        {
            facturaExistente = await context.Facturas
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Codfactura == compra.CodFactura.Value);
        }

        facturaExistente ??= await context.Facturas
            .AsNoTracking()
            .Where(f =>
                f.Idusuario == emisorOwnerId &&
                f.Notas != null &&
                EF.Functions.Like(f.Notas, $"%{marker}%"))
            .OrderByDescending(f => f.Codfactura)
            .FirstOrDefaultAsync();

        if (facturaExistente == null)
        {
            var cliente = await ConstruirClienteDesdeUsuarioAsync(context, usuario, emisorOwnerId);
            var detalleConfigurado = await ResolverDetalleCompraAsync(context, emisorOwnerId, compra);
            var factura = ConstruirFactura(
                emisorOwnerId,
                secuenciaSistema.EmisorCodigo,
                usuario.IdVendedor,
                secuenciaSistema.SerieRaw,
                compra,
                marker,
                reference,
                authorizationCode);

            var guardado = await _facturacionService.GuardarFacturaCompletaAsync(
                emisorOwnerId,
                factura,
                cliente,
                new List<Detallefactura> { detalleConfigurado });

            if (!guardado || factura.Codfactura <= 0)
            {
                return CompraDocumentosFacturaResultado.Error(
                    _facturacionService.UltimoErrorGuardarFactura ?? "No se pudo guardar la factura automática de la compra.");
            }

            facturaExistente = await context.Facturas
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Codfactura == factura.Codfactura);

            if (facturaExistente == null)
            {
                return CompraDocumentosFacturaResultado.Error("La factura se guardó pero no se pudo recuperar su registro.");
            }
        }

        mensajeSRI resultadoSri;
        try
        {
            resultadoSri = await _facturacionService.ReintentarEnvioSriFacturaAsync(facturaExistente.Codfactura);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "La factura {CodFactura} de la compra {CompraId} quedo guardada, pero fallo el envio inmediato al SRI.",
                facturaExistente.Codfactura,
                compra.Id);
            resultadoSri = new mensajeSRI
            {
                estado = facturaExistente.Estadoenviosri ?? "PENDIENTE",
                mensaje = "La factura quedo guardada y pendiente de reintento al SRI."
            };
        }

        Factura facturaActualizada;
        try
        {
            facturaActualizada = await context.Facturas
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Codfactura == facturaExistente.Codfactura)
                ?? facturaExistente;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo refrescar la factura {CodFactura}; se devolvera el registro ya guardado.", facturaExistente.Codfactura);
            facturaActualizada = facturaExistente;
        }

        var mensaje = ConstruirMensajeResultado(facturaActualizada, resultadoSri, resultadoCorreo: null);

        return new CompraDocumentosFacturaResultado
        {
            CodFactura = facturaActualizada.Codfactura,
            NumeroFactura = facturaActualizada.Numfactura,
            Autorizada = facturaActualizada.Autorizado == true,
            EstadoSri = string.IsNullOrWhiteSpace(resultadoSri.estado) ? facturaActualizada.Estadoenviosri : resultadoSri.estado,
            Mensaje = mensaje
        };
    }

    private static Factura ConstruirFactura(
        int idUsuario,
        int codEmisor,
        int? idVendedor,
        string serie,
        CompraDocumentosHistorialItem compra,
        string marker,
        string? reference,
        string? authorizationCode)
    {
        var descripcion = ObtenerDescripcionCompra(compra);
        var referenciaPago = string.Join(" / ", new[] { reference, authorizationCode }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return new Factura
        {
            Codemisor = codEmisor,
            Coddocumento = 1,
            Serie = serie,
            Fechaentrega = compra.Fecha == default ? DateTime.Now : compra.Fecha,
            Idusuario = idUsuario,
            Idvendedor = idVendedor,
            Estado = true,
            Tipopago = "19",
            Notas = string.IsNullOrWhiteSpace(referenciaPago)
                ? $"{descripcion} {marker}"
                : $"{descripcion}. Pago Pagomedios Ref/Auth: {referenciaPago}. {marker}"
        };
    }

    private async Task<Cliente> ConstruirClienteDesdeUsuarioAsync(AppDbContext context, Usuario usuario, int ownerId)
    {
        var numeroIdentificacion = NormalizarIdentificacion(usuario.Identificacion, usuario.IdTipoIdentificacion);
        var tipoIdentificacion = await ResolverCodigoIdentificacionAsync(context, usuario.IdTipoIdentificacion);
        var tipoCliente = usuario.TipoCliente ?? await ResolverTipoClienteNaturalAsync(context);

        return new Cliente
        {
            Nombres = Limpiar(usuario.Nombres),
            Apellidos = Limpiar(usuario.Apellidos),
            Nombrerazonsocial = tipoIdentificacion == "04" ? Limpiar(usuario.NombreEmpresa) ?? Limpiar(usuario.NombreCompleto) : null,
            Nombrecomercial = tipoIdentificacion == "04" ? Limpiar(usuario.NombreEmpresa) : null,
            Tipoidentificacion = tipoIdentificacion,
            Numeroidentificacion = numeroIdentificacion,
            Correo = Limpiar(usuario.Email),
            Celular = Limpiar(usuario.Celular),
            Direccion = Limpiar(usuario.DireccionEmpresa),
            TipoCliente = tipoCliente,
            Idvendedor = usuario.IdVendedor,
            Usuario = ownerId,
            Estado = true
        };
    }

    private async Task<Detallefactura> ResolverDetalleCompraAsync(AppDbContext context, int ownerId, CompraDocumentosHistorialItem compra)
    {
        var descripcion = ObtenerDescripcionCompra(compra);
        var producto = await BuscarProductoRelacionadoAsync(context, ownerId, compra);
        var tarifa = producto != null
            ? ObtenerTarifaIvaProducto(producto)
            : await ResolverTarifaPredeterminadaAsync(context, ownerId);

        var baseImponible = Math.Round(compra.MontoTotal / (1m + (tarifa / 100m)), 2, MidpointRounding.AwayFromZero);
        var valorIva = Math.Round(compra.MontoTotal - baseImponible, 2, MidpointRounding.AwayFromZero);

        return new Detallefactura
        {
            Codproducto = producto?.Codigo ?? 0,
            Codprincipal = producto?.CodigoPrincipal,
            Codauxiliar = producto?.CodAuxiliar,
            Cantproducto = 1,
            Descripproducto = producto?.Nombre ?? descripcion,
            Precioproducto = baseImponible,
            Descuento = 0m,
            Tarifa = tarifa,
            Valortproducto = baseImponible,
            Valoriva = valorIva,
            Valortotal = compra.MontoTotal
        };
    }

    private async Task<Producto?> BuscarProductoRelacionadoAsync(AppDbContext context, int ownerId, CompraDocumentosHistorialItem compra)
    {
        var palabrasClave = compra.EsIlimitado
            ? new[] { "ILIMIT", "DOCUMENT", "E-FACT" }
            : new[] { "DOCUMENT", "E-FACT", "RECARGA" };

        var productos = await context.Productos
            .AsNoTracking()
            .Where(p => p.Idusuario == ownerId && p.Estado == true && p.Nombre != null)
            .OrderByDescending(p => p.Codigo)
            .Take(50)
            .ToListAsync();

        return productos.FirstOrDefault(p =>
        {
            var nombre = (p.Nombre ?? string.Empty).Trim().ToUpperInvariant();
            return palabrasClave.Any(k => nombre.Contains(k, StringComparison.Ordinal));
        });
    }

    private async Task<int> ResolverTarifaPredeterminadaAsync(AppDbContext context, int ownerId)
    {
        var productoServicio = await context.Productos
            .AsNoTracking()
            .Where(p =>
                p.Idusuario == ownerId &&
                p.Estado == true &&
                p.Porcentajeimpuesto != null &&
                p.Tipocompravena != null &&
                EF.Functions.Like(p.Tipocompravena, "%SERV%"))
            .OrderByDescending(p => p.Codigo)
            .FirstOrDefaultAsync();

        if (productoServicio != null)
            return ObtenerTarifaIvaProducto(productoServicio);

        var ivaCatalogo = await context.Porcentajeivas
            .AsNoTracking()
            .Where(x => x.Estado == "A" || x.Estado == "1")
            .ToListAsync();

        var tarifaPositiva = ivaCatalogo
            .Select(ObtenerValorIvaCatalogo)
            .Where(v => v > 0)
            .OrderByDescending(v => v)
            .FirstOrDefault();

        return tarifaPositiva;
    }

    private static int ObtenerTarifaIvaProducto(Producto producto)
    {
        var porcentaje = TaxRateHelper.ParsePercentOrZero(producto.Porcentajeimpuesto);
        return (int)Math.Round(porcentaje, 0, MidpointRounding.AwayFromZero);
    }

    private static int ObtenerValorIvaCatalogo(Porcentajeiva iva)
    {
        if (iva.ValorCalculo.HasValue)
            return (int)Math.Round(iva.ValorCalculo.Value * 100m, 0, MidpointRounding.AwayFromZero);

        if (decimal.TryParse(iva.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var valor))
            return (int)Math.Round(valor, 0, MidpointRounding.AwayFromZero);

        return 0;
    }

    private async Task<string> ResolverCodigoIdentificacionAsync(AppDbContext context, int? idTipoIdentificacionUsuario)
    {
        var codigoPreferido = idTipoIdentificacionUsuario switch
        {
            2 => "04",
            3 => "06",
            _ => "05"
        };

        var existeCodigo = await context.Identificacion
            .AsNoTracking()
            .AnyAsync(i => i.IdeCodigo == codigoPreferido && i.Estado == true);

        if (existeCodigo)
            return codigoPreferido;

        return await context.Identificacion
            .AsNoTracking()
            .Where(i => i.Estado == true)
            .OrderBy(i => i.IdeSec)
            .Select(i => i.IdeCodigo)
            .FirstOrDefaultAsync()
            ?? codigoPreferido;
    }

    private async Task<int?> ResolverTipoClienteNaturalAsync(AppDbContext context)
    {
        var tipos = await context.Tipoclientes
            .AsNoTracking()
            .OrderBy(x => x.TclCodigo)
            .ToListAsync();

        return tipos.FirstOrDefault(x => TipoClienteClasificacion.EsNatural(x.TclDescripcion))?.TclCodigo
            ?? tipos.FirstOrDefault(x => !TipoClienteClasificacion.EsJuridica(x.TclDescripcion))?.TclCodigo
            ?? tipos.FirstOrDefault()?.TclCodigo;
    }

    private static string ConstruirMarcadorCompra(string compraId) => $"{MarcadorCompraNotas}{compraId}]";

    private static string ObtenerDescripcionCompra(CompraDocumentosHistorialItem compra)
    {
        if (!string.IsNullOrWhiteSpace(compra.Descripcion))
            return compra.Descripcion.Trim();

        return compra.EsIlimitado
            ? "Plan E-FACT de documentos ilimitados por 1 año"
            : $"Recarga de {compra.Documentos} documentos E-FACT";
    }

    private static string? Limpiar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static string NormalizarIdentificacion(string? identificacion, int? idTipoIdentificacionUsuario)
    {
        var limpio = new string((identificacion ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(limpio))
            return "9999999999999";

        return idTipoIdentificacionUsuario switch
        {
            2 when limpio.Length == 12 => "0" + limpio,
            2 => limpio,
            3 => (identificacion ?? string.Empty).Trim(),
            _ when limpio.Length == 9 => "0" + limpio,
            _ => limpio
        };
    }

    private static string ConstruirMensajeResultado(
        Factura factura,
        mensajeSRI resultadoSri,
        FacturaCorreoEnvioResultadoDto? resultadoCorreo)
    {
        var estadoSri = string.IsNullOrWhiteSpace(resultadoSri.estado)
            ? factura.Estadoenviosri ?? "PENDIENTE"
            : resultadoSri.estado;

        if (factura.Autorizado == true)
        {
            if (resultadoCorreo?.Enviado == true || resultadoCorreo?.YaEnviado == true)
                return resultadoCorreo.Mensaje;

            return "Factura emitida y autorizada correctamente.";
        }

        return string.IsNullOrWhiteSpace(resultadoSri.mensaje)
            ? $"Factura generada con estado SRI: {estadoSri}."
            : resultadoSri.mensaje;
    }
}

public sealed class CompraDocumentosFacturaResultado
{
    public bool Exito => CodFactura > 0;
    public int CodFactura { get; init; }
    public string? NumeroFactura { get; init; }
    public bool Autorizada { get; init; }
    public string? EstadoSri { get; init; }
    public string Mensaje { get; init; } = string.Empty;

    public static CompraDocumentosFacturaResultado Error(string mensaje) => new()
    {
        Mensaje = mensaje
    };
}
