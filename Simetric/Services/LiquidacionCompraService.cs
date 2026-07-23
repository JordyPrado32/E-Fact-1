using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using Simetric.Models.Glogales;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Simetric.Services;

public class LiquidacionCompraService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly LiquidacionCompraXmlGenerator _xmlGenerator;
    private readonly ILiquidacionCompraPdfService _pdfService;
    private readonly ICajaSerieResolver _cajaSerieResolver;
    private readonly EmisionControlService _emisionControlService;
    private readonly IEmailService _emailService;
    private readonly ComprobanteCorreoEstadoService _comprobanteCorreoEstadoService;
    private readonly InitialSequencePromptService _initialSequencePromptService;
    private readonly SriXmlProcessorService _sriXmlProcessorService;

    public LiquidacionCompraService(
        IDbContextFactory<AppDbContext> dbFactory,
        LiquidacionCompraXmlGenerator xmlGenerator,
        ILiquidacionCompraPdfService pdfService,
        ICajaSerieResolver cajaSerieResolver,
        EmisionControlService emisionControlService,
        IEmailService emailService,
        ComprobanteCorreoEstadoService comprobanteCorreoEstadoService,
        InitialSequencePromptService initialSequencePromptService,
        SriXmlProcessorService sriXmlProcessorService)
    {
        _dbFactory = dbFactory;
        _xmlGenerator = xmlGenerator;
        _pdfService = pdfService;
        _cajaSerieResolver = cajaSerieResolver;
        _emisionControlService = emisionControlService;
        _emailService = emailService;
        _comprobanteCorreoEstadoService = comprobanteCorreoEstadoService;
        _initialSequencePromptService = initialSequencePromptService;
        _sriXmlProcessorService = sriXmlProcessorService;
    }

    private async Task<CajaSerieResolucion> ResolverSerieLiquidacionAsync(int userId)
    {
        var resolucionBase = await _cajaSerieResolver.ResolverAsync(userId);
        var seriePreferida = await _initialSequencePromptService.GetPreferredSeriesKeyAsync(
            userId,
            "liquidacion-compra",
            resolucionBase.SerieRaw);

        if (!string.IsNullOrWhiteSpace(seriePreferida) &&
            !string.Equals(seriePreferida, resolucionBase.SerieRaw, StringComparison.Ordinal))
        {
            return await _cajaSerieResolver.ResolverAsync(userId, seriePreferida);
        }

        return resolucionBase;
    }

    public async Task<LiquidacionCompraPreviewDto> CrearPreviewManualAsync(int? usuario)
    {
        var dto = new LiquidacionCompraPreviewDto
        {
            Usuario = usuario,
            FechaEmision = DateTime.Today,
            EstaAutorizada = false,
            Detalles = new List<LiquidacionCompraDetalleDto>
            {
                CrearDetalleVacio()
            }
        };

        await using var context = await _dbFactory.CreateDbContextAsync();
        await ResolverDatosManualAsync(dto, context);
        return dto;
    }

    public async Task<List<LiquidacionCatalogoOptionDto>> ObtenerTiposIdentificacionProveedorAsync()
    {
        var codigosPermitidos = new[] { "04", "05", "06", "08" };

        await using var context = await _dbFactory.CreateDbContextAsync();

        var data = await context.Identificacion
            .AsNoTracking()
            .Where(x => x.Estado == true && codigosPermitidos.Contains(x.IdeCodigo))
            .OrderBy(x => x.IdeCodigo)
            .Select(x => new LiquidacionCatalogoOptionDto
            {
                Codigo = x.IdeCodigo,
                Descripcion = x.IdeDescripcion ?? x.IdeCodigo
            })
            .ToListAsync();

        if (data.Count > 0)
            return data;

        return new List<LiquidacionCatalogoOptionDto>
        {
            new() { Codigo = "04", Descripcion = "RUC" },
            new() { Codigo = "05", Descripcion = "Cedula" },
            new() { Codigo = "06", Descripcion = "Pasaporte" },
            new() { Codigo = "08", Descripcion = "Identificacion del exterior" }
        };
    }

    public async Task<List<LiquidacionCatalogoOptionDto>> ObtenerFormasPagoCompraAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var data = await context.FormasPago
            .AsNoTracking()
            .Where(x => x.Estado == true && x.TipoCompra == true)
            .OrderBy(x => x.Codigo)
            .Select(x => new LiquidacionCatalogoOptionDto
            {
                Codigo = x.Codigo,
                Descripcion = !string.IsNullOrWhiteSpace(x.DescripcionSri)
                    ? x.DescripcionSri!
                    : (x.Descripcion ?? x.Codigo)
            })
            .ToListAsync();

        if (data.Count > 0)
            return data;

        return new List<LiquidacionCatalogoOptionDto>
        {
            new() { Codigo = "01", Descripcion = "Sin utilizacion del sistema financiero" },
            new() { Codigo = "15", Descripcion = "Compensacion de deudas" },
            new() { Codigo = "16", Descripcion = "Tarjeta de debito" },
            new() { Codigo = "17", Descripcion = "Dinero electronico" },
            new() { Codigo = "18", Descripcion = "Tarjeta prepago" },
            new() { Codigo = "19", Descripcion = "Tarjeta de credito" },
            new() { Codigo = "20", Descripcion = "Otros con utilizacion del sistema financiero" }
        };
    }

    public async Task<List<LiquidacionProveedorLookupDto>> BuscarProveedoresAsync(string? filtro, int? usuario = null)
    {
        filtro = (filtro ?? "").Trim();
        if (string.IsNullOrWhiteSpace(filtro) && !usuario.HasValue)
            return new List<LiquidacionProveedorLookupDto>();

        var tieneFiltro = !string.IsNullOrWhiteSpace(filtro);
        var filtroLower = filtro.ToLowerInvariant();

        await using var context = await _dbFactory.CreateDbContextAsync();

        var clientes = await context.Clientes
            .AsNoTracking()
            .Where(x =>
                (x.Estado == null || x.Estado == true) &&
                (!usuario.HasValue || x.Usuario == usuario.Value) &&
                context.Proveedores.Any(p =>
                    p.estado == true &&
                    (p.ruc ?? "") == (x.Numeroidentificacion ?? "")) &&
                (
                    !tieneFiltro ||
                    (x.Numeroidentificacion ?? "").Contains(filtro) ||
                    (x.Nombrerazonsocial ?? "").ToLower().Contains(filtroLower) ||
                    (x.Nombrecomercial ?? "").ToLower().Contains(filtroLower) ||
                    (((x.Nombres ?? "") + " " + (x.Apellidos ?? "")).Trim()).ToLower().Contains(filtroLower)
                ))
            .Select(x => new LiquidacionProveedorLookupDto
            {
                CodCliente = x.Codcliente,
                Identificacion = x.Numeroidentificacion ?? "",
                TipoIdentificacion = x.Tipoidentificacion ?? "",
                RazonSocial = !string.IsNullOrWhiteSpace(x.Nombrerazonsocial)
                    ? x.Nombrerazonsocial!
                    : (!string.IsNullOrWhiteSpace(x.Nombrecomercial)
                        ? x.Nombrecomercial!
                        : (((x.Nombres ?? "") + " " + (x.Apellidos ?? "")).Trim())),
                Direccion = x.Direccion ?? "",
                TelefonoFijo = x.Telefonoconvencional ?? "",
                TelefonoMovil = x.Celular ?? "",
                Correo = x.Correo ?? ""
            })
            .Take(20)
            .ToListAsync();

        var proveedores = await context.Proveedores
            .AsNoTracking()
            .Where(x =>
                x.estado == true &&
                (!usuario.HasValue ||
                    context.Clientes.Any(c =>
                        (c.Estado == null || c.Estado == true) &&
                        c.Usuario == usuario.Value &&
                        (c.Numeroidentificacion ?? "") == (x.ruc ?? ""))) &&
                (
                    !tieneFiltro ||
                    (x.ruc ?? "").Contains(filtro) ||
                    (x.nombre ?? "").ToLower().Contains(filtroLower) ||
                    (x.nombreComercial ?? "").ToLower().Contains(filtroLower)
                ))
            .Select(x => new LiquidacionProveedorLookupDto
            {
                Identificacion = x.ruc ?? "",
                TipoIdentificacion = x.tipoIdentificacion ?? "",
                RazonSocial = !string.IsNullOrWhiteSpace(x.nombre) ? x.nombre! : (x.nombreComercial ?? ""),
                Direccion = x.direccion ?? "",
                TelefonoFijo = x.telefono ?? "",
                TelefonoMovil = x.telefonoMovil ?? "",
                Correo = x.email ?? ""
            })
            .Take(20)
            .ToListAsync();

        var combinados = clientes
            .Concat(proveedores)
            .Where(x => !string.IsNullOrWhiteSpace(x.Identificacion) || !string.IsNullOrWhiteSpace(x.RazonSocial))
            .GroupBy(x => (x.Identificacion ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => CombinarProveedorLookup(g))
            .OrderByDescending(x => EmpiezaCon(x.Identificacion, filtro))
            .ThenByDescending(x => EmpiezaCon(x.RazonSocial, filtro))
            .ThenBy(x => x.RazonSocial)
            .ThenBy(x => x.Identificacion)
            .Take(8)
            .ToList();

        return combinados;
    }

    public async Task ResolverDatosManualAsync(LiquidacionCompraPreviewDto preview)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        await ResolverDatosManualAsync(preview, context);
    }

    private async Task ResolverDatosManualAsync(LiquidacionCompraPreviewDto preview, AppDbContext context)
    {
        if (preview == null)
            return;

        preview.TipoIdentificacionProveedorNombre =
            await ObtenerDescripcionTipoIdentificacionAsync(preview.TipoIdentificacionProveedor, context);

        preview.FormaPagoNombre =
            await ObtenerDescripcionFormaPagoAsync(preview.FormaPago, context);

        var emisor = await BuscarEmisorAsync(preview, context);
        if (emisor != null)
        {
            preview.CodEmisor = emisor.Codigo;
            preview.RucEmisor = emisor.Ruc?.Trim() ?? preview.RucEmisor;
            preview.RazonSocialEmisor = emisor.RazonSocial?.Trim() ?? "";
            preview.NombreComercialEmisor = emisor.NomComercial?.Trim() ?? preview.RazonSocialEmisor;
            preview.NombreEmisorEncontrado = $"{preview.NombreComercialEmisor} - {preview.RucEmisor}";
            preview.DireccionEstablecimiento =
                emisor.DirEstablecimiento?.Trim()
                ?? emisor.DireccionMatriz?.Trim()
                ?? "";
            preview.DireccionMatriz = preview.DireccionEstablecimiento;
            preview.EmailEmisor = emisor.Email?.Trim() ?? "";
            preview.TelefonoEmisor = emisor.Telefono?.Trim() ?? "";
            preview.LogoEmisor = emisor.LogoImagen?.Trim() ?? "";
            preview.ContribuyenteEspecial = emisor.ContribuyenteEspecial?.Trim() ?? "";
            preview.ObligadoContabilidad = string.IsNullOrWhiteSpace(emisor.LlevaContabilidad)
                ? "NO"
                : emisor.LlevaContabilidad!.Trim().ToUpperInvariant();

            if (int.TryParse(emisor.TipoAmbiente, out var ambienteEmisor))
                preview.Ambiente = ambienteEmisor;
        }
        else
        {
            preview.CodEmisor = null;
            preview.NombreEmisorEncontrado = "";
            preview.RazonSocialEmisor = "";
            preview.NombreComercialEmisor = "";
            preview.DireccionMatriz = "";
            preview.DireccionEstablecimiento = "";
            preview.LogoEmisor = "";
            preview.ContribuyenteEspecial = "";
            preview.ObligadoContabilidad = "NO";
        }

        var serieCaja = await ObtenerSerieComprasAsync(preview.Usuario, emisor, context);
        if (!string.IsNullOrWhiteSpace(serieCaja))
        {
            var (estab, ptoEmi) = SepararSerie(serieCaja);

            if (DebeSobrescribirSerie(preview.Estab))
                preview.Estab = estab;

            if (DebeSobrescribirSerie(preview.PtoEmi))
                preview.PtoEmi = ptoEmi;
        }

        if (string.IsNullOrWhiteSpace(preview.Secuencial))
            preview.Secuencial = await GenerarSiguienteSecuencialAsync(preview.CodEmisor, preview.Serie, context);
        else
            preview.Secuencial = NormalizarSecuencial(preview.Secuencial);

        await ResolverProveedorAsync(preview, context);
        RecalcularTotalesDesdeDetalles(preview);
    }

    public LiquidacionCompraDetalleDto CrearDetalleVacio()
    {
        return new LiquidacionCompraDetalleDto
        {
            Cantidad = 1m,
            PorcentajeDescuento = 0m,
            CodigoPorcentaje = 4,
            Tarifa = 15
        };
    }

    public void RecalcularDetalle(LiquidacionCompraDetalleDto item)
    {
        if (item == null)
            return;

        item.Cantidad = Red2(item.Cantidad);
        item.PrecioUnitario = Red2(item.PrecioUnitario);
        item.PorcentajeDescuento = Math.Clamp(Red2(item.PorcentajeDescuento), 0m, 100m);
        item.Descuento = Red2(item.Descuento);

        if (item.Cantidad < 0m) item.Cantidad = 0m;
        if (item.PrecioUnitario < 0m) item.PrecioUnitario = 0m;
        if (item.Descuento < 0m) item.Descuento = 0m;

        if (item.PorcentajeDescuento > 0m)
        {
            var subtotalBruto = ObtenerSubtotalBrutoDetalle(item);
            item.Descuento = Red2(subtotalBruto * (item.PorcentajeDescuento / 100m));
        }

        item.PrecioTotalSinImpuesto = Red2((item.Cantidad * item.PrecioUnitario) - item.Descuento);
        if (item.PrecioTotalSinImpuesto < 0m)
            item.PrecioTotalSinImpuesto = 0m;

        item.Tarifa = ObtenerTarifaDesdeCodigo(item.CodigoPorcentaje);
        item.ValorIva = Red2(CalcularValorIva(item.CodigoPorcentaje, item.PrecioTotalSinImpuesto));
        item.ValorTotal = Red2(item.PrecioTotalSinImpuesto + item.ValorIva);
    }

    public decimal ObtenerSubtotalBrutoDetalle(LiquidacionCompraDetalleDto item)
    {
        if (item == null)
            return 0m;

        return Red2(item.Cantidad * item.PrecioUnitario);
    }

    public decimal ObtenerDescuentoUnitarioDetalle(LiquidacionCompraDetalleDto item)
    {
        if (item == null)
            return 0m;

        var bruto = ObtenerSubtotalBrutoDetalle(item);
        if (bruto <= 0m)
            return 0m;

        if (item.PorcentajeDescuento > 0m)
            return Red2(bruto * (item.PorcentajeDescuento / 100m));

        return Red2(item.Descuento);
    }

    public decimal ObtenerDescuentoGlobalDetalle(LiquidacionCompraPreviewDto preview, LiquidacionCompraDetalleDto item)
    {
        if (preview == null || item == null)
            return 0m;

        if (ObtenerDescuentoUnitarioDetalle(item) > 0m)
            return 0m;

        if (preview.DescuentoGlobalPorcentaje <= 0m)
            return 0m;

        return Red2(ObtenerSubtotalBrutoDetalle(item) * (preview.DescuentoGlobalPorcentaje / 100m));
    }

    public decimal ObtenerDescuentoEfectivoDetalle(LiquidacionCompraPreviewDto preview, LiquidacionCompraDetalleDto item)
    {
        if (item == null)
            return 0m;

        var descuentoUnitario = ObtenerDescuentoUnitarioDetalle(item);
        if (descuentoUnitario > 0m)
            return descuentoUnitario;

        return ObtenerDescuentoGlobalDetalle(preview, item);
    }

    public decimal ObtenerBaseImponibleDetalle(LiquidacionCompraPreviewDto preview, LiquidacionCompraDetalleDto item)
    {
        return Math.Max(0m, Red2(ObtenerSubtotalBrutoDetalle(item) - ObtenerDescuentoEfectivoDetalle(preview, item)));
    }

    public decimal ObtenerIvaDetalle(LiquidacionCompraPreviewDto preview, LiquidacionCompraDetalleDto item)
    {
        if (item == null)
            return 0m;

        return Red2(CalcularValorIva(item.CodigoPorcentaje, ObtenerBaseImponibleDetalle(preview, item)));
    }

    public decimal ObtenerTotalDetalle(LiquidacionCompraPreviewDto preview, LiquidacionCompraDetalleDto item)
    {
        return Red2(ObtenerBaseImponibleDetalle(preview, item) + ObtenerIvaDetalle(preview, item));
    }

    public void RecalcularTotalesDesdeDetalles(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            return;

        preview.Detalles ??= new List<LiquidacionCompraDetalleDto>();

        preview.Subtotal0 = 0m;
        preview.Subtotal5 = 0m;
        preview.Subtotal8 = 0m;
        preview.Subtotal15 = 0m;
        preview.NoImp = 0m;
        preview.ExIva = 0m;
        preview.TotalDescuento = 0m;
        preview.Iva5 = 0m;
        preview.Iva8 = 0m;
        preview.Iva15 = 0m;
        preview.IvaTotal = 0m;
        preview.TotalSinImpuestos = 0m;
        preview.ImporteTotal = 0m;

        foreach (var item in preview.Detalles)
        {
            RecalcularDetalle(item);
            var baseImponible = ObtenerBaseImponibleDetalle(preview, item);
            var valorIva = ObtenerIvaDetalle(preview, item);
            var descuentoEfectivo = ObtenerDescuentoEfectivoDetalle(preview, item);

            item.Tarifa = ObtenerTarifaDesdeCodigo(item.CodigoPorcentaje);
            item.PrecioTotalSinImpuesto = baseImponible;
            item.ValorIva = valorIva;
            item.ValorTotal = Red2(baseImponible + valorIva);

            preview.TotalDescuento += descuentoEfectivo;

            switch (item.CodigoPorcentaje)
            {
                case 5:
                    preview.Subtotal5 += baseImponible;
                    preview.Iva5 += valorIva;
                    break;
                case 8:
                    preview.Subtotal8 += baseImponible;
                    preview.Iva8 += valorIva;
                    break;
                case 4:
                    preview.Subtotal15 += baseImponible;
                    preview.Iva15 += valorIva;
                    break;
                case 6:
                    preview.NoImp += baseImponible;
                    break;
                case 7:
                    preview.ExIva += baseImponible;
                    break;
                default:
                    preview.Subtotal0 += baseImponible;
                    break;
            }
        }

        preview.Subtotal0 = Red2(preview.Subtotal0);
        preview.Subtotal5 = Red2(preview.Subtotal5);
        preview.Subtotal8 = Red2(preview.Subtotal8);
        preview.Subtotal15 = Red2(preview.Subtotal15);
        preview.NoImp = Red2(preview.NoImp);
        preview.ExIva = Red2(preview.ExIva);
        preview.TotalDescuento = Red2(preview.TotalDescuento);
        preview.Iva5 = Red2(preview.Iva5);
        preview.Iva8 = Red2(preview.Iva8);
        preview.Iva15 = Red2(preview.Iva15);

        preview.TotalSinImpuestos = Red2(
            preview.Subtotal0 +
            preview.Subtotal5 +
            preview.Subtotal8 +
            preview.Subtotal15 +
            preview.NoImp +
            preview.ExIva);

        preview.IvaTotal = Red2(preview.Iva5 + preview.Iva8 + preview.Iva15);
        preview.ImporteTotal = Red2(preview.TotalSinImpuestos + preview.IvaTotal);
    }

    public LiquidacionCompraPreviewDto CrearPreviewPersistible(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));

        var copia = new LiquidacionCompraPreviewDto
        {
            Usuario = preview.Usuario,
            ClaveAcceso = preview.ClaveAcceso,
            NumeroAutorizacion = preview.NumeroAutorizacion,
            EstaAutorizada = preview.EstaAutorizada,
            Ambiente = preview.Ambiente,
            Estab = preview.Estab,
            PtoEmi = preview.PtoEmi,
            Secuencial = preview.Secuencial,
            FechaEmision = preview.FechaEmision,
            CodEmisor = preview.CodEmisor,
            NombreEmisorEncontrado = preview.NombreEmisorEncontrado,
            RucEmisor = preview.RucEmisor,
            RazonSocialEmisor = preview.RazonSocialEmisor,
            NombreComercialEmisor = preview.NombreComercialEmisor,
            DireccionMatriz = preview.DireccionMatriz,
            DireccionEstablecimiento = preview.DireccionEstablecimiento,
            ContribuyenteEspecial = preview.ContribuyenteEspecial,
            ObligadoContabilidad = preview.ObligadoContabilidad,
            EmailEmisor = preview.EmailEmisor,
            TelefonoEmisor = preview.TelefonoEmisor,
            LogoEmisor = preview.LogoEmisor,
            TipoIdentificacionProveedor = preview.TipoIdentificacionProveedor,
            TipoIdentificacionProveedorNombre = preview.TipoIdentificacionProveedorNombre,
            IdentificacionProveedor = preview.IdentificacionProveedor,
            RazonSocialProveedor = preview.RazonSocialProveedor,
            DireccionProveedor = preview.DireccionProveedor,
            TelefonoFijoProveedor = preview.TelefonoFijoProveedor,
            TelefonoProveedor = preview.TelefonoProveedor,
            EmailProveedor = preview.EmailProveedor,
            CorreosAdicionalesProveedor = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(preview.CorreosAdicionalesProveedor),
            CorreosAdicionalesProveedorGuardar = ComprobanteCorreoDestinatariosHelper.NormalizarCorreos(preview.CorreosAdicionalesProveedorGuardar),
            CodProveedor = preview.CodProveedor,
            EsClienteProveedor = preview.EsClienteProveedor,
            EsProveedorProveedor = preview.EsProveedorProveedor,
            SegmentoCliente = preview.SegmentoCliente,
            CuentaContableCliente = preview.CuentaContableCliente,
            FuenteOrigenCliente = preview.FuenteOrigenCliente,
            TieneLimiteCreditoCliente = preview.TieneLimiteCreditoCliente,
            CuentaContableProveedor = preview.CuentaContableProveedor,
            CodigoProveedorInterno = preview.CodigoProveedorInterno,
            CreditoTributarioProveedor = preview.CreditoTributarioProveedor,
            EsSujetoRetencionProveedor = preview.EsSujetoRetencionProveedor,
            RegistraInformacionBancariaProveedor = preview.RegistraInformacionBancariaProveedor,
            BancoProveedor = preview.BancoProveedor,
            TipoCuentaProveedor = preview.TipoCuentaProveedor,
            NumeroCuentaProveedor = preview.NumeroCuentaProveedor,
            FormaPago = preview.FormaPago,
            FormaPagoNombre = preview.FormaPagoNombre,
            Plazo = preview.Plazo,
            UnidadTiempo = preview.UnidadTiempo,
            Moneda = preview.Moneda,
            DescuentoGlobalPorcentaje = 0m,
            Detalles = new List<LiquidacionCompraDetalleDto>()
        };

        foreach (var item in preview.Detalles ?? new List<LiquidacionCompraDetalleDto>())
        {
            if (EsDetalleVacio(item))
                continue;

            var bruto = ObtenerSubtotalBrutoDetalle(item);
            var descuentoEfectivo = ObtenerDescuentoEfectivoDetalle(preview, item);
            var baseImponible = Math.Max(0m, Red2(bruto - descuentoEfectivo));
            var valorIva = Red2(CalcularValorIva(item.CodigoPorcentaje, baseImponible));

            copia.Detalles.Add(new LiquidacionCompraDetalleDto
            {
                CodProducto = item.CodProducto,
                CodPrincipal = item.CodPrincipal,
                CodAuxiliar = item.CodAuxiliar,
                Descripcion = item.Descripcion,
                Cantidad = Red2(item.Cantidad),
                PrecioUnitario = Red2(item.PrecioUnitario),
                PorcentajeDescuento = item.PorcentajeDescuento,
                Descuento = descuentoEfectivo,
                PrecioTotalSinImpuesto = baseImponible,
                CodigoPorcentaje = item.CodigoPorcentaje,
                Tarifa = ObtenerTarifaDesdeCodigo(item.CodigoPorcentaje),
                ValorIva = valorIva,
                ValorTotal = Red2(baseImponible + valorIva)
            });
        }

        RecalcularTotalesDesdeDetalles(copia);
        return copia;
    }

    public async Task<int> GuardarLiquidacionAsync(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay datos para guardar.");

        await _emisionControlService.AsegurarPuedeEmitirAsync(preview.Usuario);

        preview.Detalles ??= new List<LiquidacionCompraDetalleDto>();

        await using var context = await _dbFactory.CreateDbContextAsync();
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                await ResolverDatosManualAsync(preview, context);

                var persistible = CrearPreviewPersistible(preview);

                if (string.IsNullOrWhiteSpace(persistible.ClaveAcceso))
                    persistible.ClaveAcceso = GenerarClaveAccesoTemporal(persistible);

                if (string.IsNullOrWhiteSpace(persistible.NumeroAutorizacion))
                    persistible.NumeroAutorizacion = persistible.ClaveAcceso;

                ValidarLiquidacion(persistible);

                if (!persistible.CodEmisor.HasValue || persistible.CodEmisor.Value <= 0)
                    throw new Exception("No se encontro un emisor valido para registrar la liquidacion.");

                var duplicada = await context.ComprasFacturas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.Estado == true &&
                        x.CodEmisor == persistible.CodEmisor &&
                        (
                            x.CodClave == persistible.ClaveAcceso ||
                            (
                                x.CodDocumento == "03" &&
                                (x.Serie ?? "") == persistible.Serie &&
                                (x.NumFactura ?? "") == persistible.Secuencial
                            )
                        ));

                if (duplicada != null)
                    throw new Exception($"Ya existe una liquidacion registrada con la serie {persistible.SerieVisual} y secuencial {persistible.Secuencial}.");

                var emisor = await context.Emisores
                    .FirstOrDefaultAsync(x => x.Codigo == persistible.CodEmisor.Value);

                if (emisor == null)
                    throw new Exception("No se encontro el emisor relacionado con la liquidacion.");

                var clienteProveedor = await ObtenerOCrearClienteProveedorAsync(persistible, context);
                await SincronizarCorreosAdicionalesProveedorAsync(persistible, clienteProveedor, context);
                await ObtenerOCrearProveedorAsync(persistible, context);
                var idVendedor = await context.Usuarios
                    .AsNoTracking()
                    .Where(x => x.IdUsuario == persistible.Usuario)
                    .Select(x => x.IdVendedor)
                    .FirstOrDefaultAsync();

                var compra = new ComprasFactura
                {
                    CodClave = persistible.ClaveAcceso,
                    CodClientes = clienteProveedor?.Codcliente,
                    CodEmisor = emisor.Codigo,
                    CodDocumento = "03",
                    NumFactura = persistible.Secuencial,
                    NumAutorizacion = persistible.NumeroAutorizacion,
                    FchAutorizacion = persistible.FechaEmision,
                    Subtotal12 = persistible.Subtotal15,
                    Subtotal0 = persistible.Subtotal0,
                    Subtotal = persistible.TotalSinImpuestos,
                    Descuentos = persistible.TotalDescuento,
                    Iva = persistible.IvaTotal,
                    ValorTotal = persistible.ImporteTotal,
                    FechaVence = CalcularFechaVence(persistible.FechaEmision, persistible.Plazo),
                    NoImp = persistible.NoImp,
                    ExIva = persistible.ExIva,
                    ValorICE = 0m,
                    Usuario = persistible.Usuario,
                    IdVendedor = idVendedor,
                    Autorizado = "0",
                    Mensaje = "Liquidacion de compra manual registrada",
                    IdEmpresa = emisor.IdEmpresa,
                    IdSucursal = emisor.IdSucursal,
                    Serie = persistible.Serie,
                    FechaAutoSRI = persistible.FechaEmision?.ToString("dd/MM/yyyy"),
                    TipoPago = string.IsNullOrWhiteSpace(persistible.FormaPago) ? "20" : persistible.FormaPago,
                    EstadoEnvioSRI = "MANUAL",
                    SubCeroTotal = persistible.Subtotal0,
                    SubDoceTotal = persistible.Subtotal15,
                    SubNoImpTotal = persistible.NoImp,
                    SubExIvaTotal = persistible.ExIva,
                    Ambiente = persistible.Ambiente,
                    Estado = true,
                    TiempoCredito = persistible.Plazo ?? 0,
                    TipoDocumento = "LIQ",
                    FechaRegistro = DateTime.Now,
                    Inventario = false,
                    Contabilizado = false,
                    Subtotal5 = persistible.Subtotal5,
                    Subtotal8 = persistible.Subtotal8,
                    Iva5 = persistible.Iva5,
                    Iva8 = persistible.Iva8,
                    TieneRetencion = false
                };

                context.ComprasFacturas.Add(compra);
                await context.SaveChangesAsync();

                foreach (var item in persistible.Detalles)
                {
                    var detalle = new ComprasDetalleFac
                    {
                        CodFactura = compra.CodFactura,
                        CodProducto = item.CodProducto > 0 ? item.CodProducto : 0,
                        CodPrincipal = item.CodPrincipal,
                        CodAuxiliar = item.CodAuxiliar,
                        CantProducto = item.Cantidad,
                        DescripProducto = item.Descripcion,
                        PrecioProducto = item.PrecioUnitario,
                        Descuento = item.Descuento,
                        ValorTProducto = item.PrecioTotalSinImpuesto,
                        ValorIVA = item.ValorIva,
                        ValorTotal = item.ValorTotal,
                        CodImp = 2,
                        PorImp = item.CodigoPorcentaje,
                        Tarifa = item.Tarifa,
                        Inventariado = false,
                        Observacion = $"Liquidacion manual - Moneda: {persistible.Moneda}"
                    };

                    context.ComprasDetalleFac.Add(detalle);
                }

                await context.SaveChangesAsync();
                await _emisionControlService.ConsumirDocumentoAsync(context, persistible.Usuario);
                await transaction.CommitAsync();
                return compra.CodFactura;
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { }
                throw;
            }
        });
    }

    public async Task<LiquidacionCompraGuardadoResultadoDto> GuardarLiquidacionConArchivosAsync(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay datos para guardar.");

        var codFactura = await GuardarLiquidacionAsync(preview);
        var resultado = new LiquidacionCompraGuardadoResultadoDto
        {
            CodFactura = codFactura
        };

        if (preview.Usuario.HasValue)
        {
            try
            {
                resultado.XmlUrl = await AsegurarXmlLiquidacionUsuarioAsync(codFactura, preview.Usuario.Value);
                if (string.IsNullOrWhiteSpace(resultado.XmlUrl))
                    resultado.ProblemasArchivos.Add("XML: No se pudo generar el archivo.");
            }
            catch (Exception ex)
            {
                resultado.ProblemasArchivos.Add($"XML: {ex.Message}");
            }

            try
            {
                resultado.PdfUrl = await AsegurarPdfLiquidacionUsuarioAsync(codFactura, preview.Usuario.Value);
                if (string.IsNullOrWhiteSpace(resultado.PdfUrl))
                    resultado.ProblemasArchivos.Add("PDF: No se pudo generar el archivo.");
            }
            catch (Exception ex)
            {
                resultado.ProblemasArchivos.Add($"PDF: {ex.Message}");
            }

            return resultado;
        }

        var previewPersistible = CrearPreviewPersistible(preview);

        if (string.IsNullOrWhiteSpace(previewPersistible.ClaveAcceso))
            previewPersistible.ClaveAcceso = GenerarClaveAccesoTemporal(previewPersistible);

        try
        {
            var nombreXml = await _xmlGenerator.GenerarXmlTemporalAsync(previewPersistible);
            resultado.XmlUrl = ConstruirLiquidacionXmlUrl(Path.GetFileNameWithoutExtension(nombreXml).Replace("LIQ_", ""));
            if (string.IsNullOrWhiteSpace(resultado.XmlUrl))
                resultado.ProblemasArchivos.Add("XML: No se pudo generar el archivo.");
        }
        catch (Exception ex)
        {
            resultado.ProblemasArchivos.Add($"XML: {ex.Message}");
        }

        try
        {
            resultado.PdfUrl = await _pdfService.GenerarPdfLiquidacionAsync(previewPersistible);
            if (string.IsNullOrWhiteSpace(resultado.PdfUrl))
                resultado.ProblemasArchivos.Add("PDF: No se pudo generar el archivo.");
        }
        catch (Exception ex)
        {
            resultado.ProblemasArchivos.Add($"PDF: {ex.Message}");
        }

        return resultado;
    }

    public async Task<List<LiquidacionCompraListDto>> ListarLiquidacionesUsuarioAsync(int idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var data = await (
            from compra in context.ComprasFacturas.AsNoTracking()
            join proveedor in context.Clientes.AsNoTracking()
                on compra.CodClientes equals proveedor.Codcliente into proveedorJoin
            from proveedor in proveedorJoin.DefaultIfEmpty()

            where compra.Estado == true &&
                  compra.CodDocumento == "03" &&
                  compra.Usuario == idUsuario
            orderby compra.CodFactura descending
            select new
            {
                Compra = compra,
                Proveedor = proveedor
            })
            .ToListAsync();

        var codigosCompra = data.Select(x => x.Compra.CodFactura).ToList();
        var comprasConRetencionDetalle = await context.ComprasRetValor
            .AsNoTracking()
            .Where(x => x.IdCompra.HasValue && codigosCompra.Contains(x.IdCompra.Value))
            .Select(x => x.IdCompra!.Value)
            .Distinct()
            .ToListAsync();
        var comprasConRetencionSet = comprasConRetencionDetalle.ToHashSet();

        return data.Select(x => new LiquidacionCompraListDto
        {
            CodFactura = x.Compra.CodFactura,
            Serie = x.Compra.Serie ?? "",
            Secuencial = x.Compra.NumFactura ?? "",
            FechaEmision = x.Compra.FchAutorizacion ?? x.Compra.FechaRegistro,
            Proveedor = ObtenerNombreClienteLiquidacion(x.Proveedor),
            IdentificacionProveedor = x.Proveedor?.Numeroidentificacion ?? "",
            EstadoSri = x.Compra.EstadoEnvioSRI ?? "",
            Autorizado = x.Compra.Autorizado ?? "",
            NumeroAutorizacion = x.Compra.NumAutorizacion ?? "",
            MensajeSri = x.Compra.Mensaje ?? "",
            TotalSinImpuestos = x.Compra.Subtotal ?? 0m,
            IvaTotal = x.Compra.Iva ?? 0m,
            ImporteTotal = x.Compra.ValorTotal ?? 0m,
            ClaveAcceso = x.Compra.CodClave ?? "",
            XmlUrl = ConstruirLiquidacionXmlUrl(x.Compra.CodClave),
            PdfUrl = ConstruirLiquidacionPdfUrl(x.Compra.CodClave),
            TieneRetencion = x.Compra.TieneRetencion == true ||
                !string.IsNullOrWhiteSpace(x.Compra.NumRetencion) ||
                comprasConRetencionSet.Contains(x.Compra.CodFactura),
            NumeroRetencion = x.Compra.NumRetencion ?? ""
        }).ToList();
    }

    public async Task<LiquidacionCompraDetalleViewDto?> GetLiquidacionDetalleUsuarioAsync(int codFactura, int idUsuario)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var compra = await context.ComprasFacturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CodFactura == codFactura &&
                x.Usuario == idUsuario &&
                x.Estado == true &&
                x.CodDocumento == "03");

        if (compra == null)
            return null;

        return await ConstruirDetalleLiquidacionAsync(compra, context);
    }

    public async Task<string?> AsegurarXmlLiquidacionUsuarioAsync(int codFactura, int idUsuario)
    {
        var detalle = await GetLiquidacionDetalleUsuarioAsync(codFactura, idUsuario);
        if (detalle == null)
            return null;

        var clave = detalle.Preview.ClaveAcceso;
        if (string.IsNullOrWhiteSpace(clave))
        {
            detalle.Preview.ClaveAcceso = GenerarClaveAccesoTemporal(detalle.Preview);
            clave = detalle.Preview.ClaveAcceso;
        }

        var rutaLocal = ConstruirLiquidacionXmlRutaLocal(clave);
        if (!File.Exists(rutaLocal))
            await _xmlGenerator.GenerarXmlTemporalAsync(detalle.Preview);

        return ConstruirLiquidacionXmlUrl(clave);
    }

    public async Task<string?> AsegurarPdfLiquidacionUsuarioAsync(int codFactura, int idUsuario, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        var detalle = await GetLiquidacionDetalleUsuarioAsync(codFactura, idUsuario);
        if (detalle == null)
            return null;

        var clave = detalle.Preview.ClaveAcceso;
        if (string.IsNullOrWhiteSpace(clave))
        {
            detalle.Preview.ClaveAcceso = GenerarClaveAccesoTemporal(detalle.Preview);
            clave = detalle.Preview.ClaveAcceso;
        }

        await _pdfService.GenerarPdfLiquidacionAsync(detalle.Preview, formato);

        return ConstruirLiquidacionPdfUrl(clave, formato);
    }

    public async Task ActualizarAutorizacionLiquidacionAsync(int codFactura, string? numeroAutorizacion, string? fechaAutorizacion, string? mensaje, string? autorizado)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compra = await context.ComprasFacturas.FirstOrDefaultAsync(x =>
            x.CodFactura == codFactura &&
            x.CodDocumento == "03");

        if (compra == null)
            throw new Exception($"No se encontró la liquidación {codFactura} para actualizar su autorización.");

        compra.NumAutorizacion = string.IsNullOrWhiteSpace(numeroAutorizacion)
            ? compra.NumAutorizacion
            : numeroAutorizacion.Trim();
        compra.FechaAutoSRI = string.IsNullOrWhiteSpace(fechaAutorizacion)
            ? compra.FechaAutoSRI
            : fechaAutorizacion;
        compra.Mensaje = string.IsNullOrWhiteSpace(mensaje)
            ? compra.Mensaje
            : mensaje;
        compra.EstadoEnvioSRI = ResolverEstadoSriLiquidacion(autorizado, compra.EstadoEnvioSRI);
        compra.Autorizado = ResolverBanderaAutorizacionLiquidacion(autorizado, compra.Autorizado);

        if (LiquidacionCompraEstaAutorizada(compra.Autorizado, compra.EstadoEnvioSRI))
            compra.FchAutorizacion = DateTime.Now;

        await context.SaveChangesAsync();

        if (compra.Usuario is > 0)
        {
            try
            {
                await AsegurarPdfLiquidacionUsuarioAsync(codFactura, compra.Usuario.Value);
            }
            catch
            {
            }
        }
    }

    public async Task<mensajeSRI> EmitirLiquidacionSriAsync(int codFactura, int? idUsuario = null, bool intentarEnviarCorreo = true)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var compra = await context.ComprasFacturas
            .Include(x => x.Detalles)
            .FirstOrDefaultAsync(x =>
                x.CodFactura == codFactura &&
                x.Estado == true &&
                x.CodDocumento == "03");

        if (compra == null)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se encontró la liquidación de compra para enviar al SRI."
            };
        }

        if (idUsuario.HasValue && compra.Usuario != idUsuario.Value)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "La liquidación de compra no pertenece al usuario actual."
            };
        }

        if (LiquidacionCompraEstaAutorizada(compra.Autorizado, compra.EstadoEnvioSRI))
        {
            if (intentarEnviarCorreo)
                await IntentarEnviarLiquidacionPorCorreoAsync(codFactura);

            return new mensajeSRI
            {
                estado = DocumentoAutorizacionHelper.EstadoAutorizado,
                autorizacion = compra.NumAutorizacion ?? string.Empty,
                fecha = compra.FechaAutoSRI ?? compra.FchAutorizacion?.ToString(CultureInfo.InvariantCulture),
                mensaje = "La liquidación de compra ya se encuentra autorizada."
            };
        }

        if (ComprobanteReenvioFechaHelper.PuedeRenovarFecha(compra.EstadoEnvioSRI, compra.Mensaje) &&
            ComprobanteReenvioFechaHelper.DebeActualizar(compra.FchAutorizacion ?? compra.FechaRegistro, compra.CodClave))
        {
            var fechaAnterior = compra.FchAutorizacion ?? compra.FechaRegistro ?? DateTime.Today;
            if (!string.IsNullOrWhiteSpace(compra.CodClave))
            {
                var xmlAnterior = ConstruirLiquidacionXmlRutaLocal(compra.CodClave);
                if (File.Exists(xmlAnterior))
                    File.Delete(xmlAnterior);
            }

            compra.FechaVence = ComprobanteReenvioFechaHelper.DesplazarFecha(compra.FechaVence, fechaAnterior);
            compra.FchAutorizacion = DateTime.Today;
            compra.CodClave = null;
            compra.NumAutorizacion = null;
            compra.FechaAutoSRI = null;
            await context.SaveChangesAsync();
        }

        var detalle = await ConstruirDetalleLiquidacionAsync(compra, context);
        if (detalle == null)
        {
            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se pudo construir el detalle de la liquidación para el envío al SRI."
            };
        }

        var emisor = detalle.Emisor;
        if (emisor == null)
        {
            await ActualizarAutorizacionLiquidacionAsync(
                codFactura,
                string.Empty,
                DateTime.Now.ToString("O"),
                "No se encontró el emisor de la liquidación de compra.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se encontró el emisor asociado a la liquidación de compra."
            };
        }

        if (string.IsNullOrWhiteSpace(emisor.PathCertificado) || string.IsNullOrWhiteSpace(emisor.ClaveCertificado))
        {
            await ActualizarAutorizacionLiquidacionAsync(
                codFactura,
                string.Empty,
                DateTime.Now.ToString("O"),
                "El emisor no tiene configurada una firma electrónica válida para emitir la liquidación de compra.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "El emisor no tiene configurado el certificado electrónico requerido para el envío al SRI."
            };
        }

        var rutaXml = await AsegurarRutaXmlLiquidacionAsync(codFactura, detalle);
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            await ActualizarAutorizacionLiquidacionAsync(
                codFactura,
                string.Empty,
                DateTime.Now.ToString("O"),
                "No se pudo generar el XML de la liquidación de compra para el envío al SRI.",
                "ERROR INTERNO");

            return new mensajeSRI
            {
                estado = "ERROR",
                mensaje = "No se pudo generar el XML de la liquidación de compra para el envío al SRI."
            };
        }

        var respuestaSri = await _sriXmlProcessorService.ProcessXmlAsync(
            rutaXml,
            emisor.PathCertificado,
            emisor.ClaveCertificado);

        var fechaRespuesta = string.IsNullOrWhiteSpace(respuestaSri.fecha)
            ? DateTime.Now.ToString("O")
            : respuestaSri.fecha;

        if (string.Equals(respuestaSri.estado, DocumentoAutorizacionHelper.EstadoAutorizado, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(respuestaSri.autorizacion))
        {
            await ActualizarAutorizacionLiquidacionAsync(
                codFactura,
                respuestaSri.autorizacion ?? string.Empty,
                fechaRespuesta,
                "ok",
                DocumentoAutorizacionHelper.EstadoAutorizado);

            if (compra.Usuario is > 0)
                await AsegurarPdfLiquidacionUsuarioAsync(codFactura, compra.Usuario.Value);

            if (intentarEnviarCorreo)
                await IntentarEnviarLiquidacionPorCorreoAsync(codFactura, rutaXml);

            return respuestaSri;
        }

        await ActualizarAutorizacionLiquidacionAsync(
            codFactura,
            respuestaSri.autorizacion ?? string.Empty,
            fechaRespuesta,
            string.IsNullOrWhiteSpace(respuestaSri.mensaje) ? respuestaSri.estado : respuestaSri.mensaje,
            string.IsNullOrWhiteSpace(respuestaSri.estado) ? "PENDIENTE" : respuestaSri.estado);

        return respuestaSri;
    }

    public async Task<FacturaCorreoEnvioResultadoDto> IntentarEnviarLiquidacionPorCorreoAsync(
        int codFactura,
        string? rutaXmlExistente = null,
        string? rutaPdfExistente = null)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var compra = await context.ComprasFacturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CodFactura == codFactura &&
                x.Estado == true &&
                x.CodDocumento == "03");

        if (compra == null)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "No se encontró la liquidación de compra para enviar por correo."
            };
        }

        var seguimiento = await _comprobanteCorreoEstadoService.GetEstadoAsync(
            ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
            codFactura);
        if (seguimiento?.CorreoEnviado == true)
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                YaEnviado = true,
                Mensaje = "El correo de esta liquidación de compra ya fue enviado anteriormente.",
                TotalDestinatarios = 0
            };
        }

        var detalle = await ConstruirDetalleLiquidacionAsync(compra, context);
        if (detalle == null)
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura,
                "No se pudo cargar el detalle de la liquidación de compra para enviar el correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                Mensaje = "No se pudo cargar el detalle de la liquidación de compra para enviar el correo."
            };
        }

        var destinatarios = await ComprobanteCorreoDestinatariosHelper.ConstruirDestinatariosClienteAsync(
            context,
            compra.Usuario,
            compra.CodClientes,
            detalle.Preview.EmailProveedor);

        if (!destinatarios.Any())
        {
            return new FacturaCorreoEnvioResultadoDto
            {
                SinDestinatarios = true,
                Mensaje = "La liquidación de compra no tiene correos configurados para el envío."
            };
        }

        if (!LiquidacionCompraEstaAutorizada(compra.Autorizado, compra.EstadoEnvioSRI))
        {
            await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura);

            return new FacturaCorreoEnvioResultadoDto
            {
                PendienteAutorizacion = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"La liquidación de compra aún no está autorizada. El correo queda pendiente para {destinatarios.Count} destinatario(s) hasta que el documento refleje un estado aprobado."
            };
        }

        var claveAcceso = detalle.Preview.ClaveAcceso;
        if (string.IsNullOrWhiteSpace(claveAcceso))
        {
            claveAcceso = GenerarClaveAccesoTemporal(detalle.Preview);
            detalle.Preview.ClaveAcceso = claveAcceso;
        }

        var rutaXml = rutaXmlExistente;
        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            rutaXml = ConstruirLiquidacionXmlRutaLocal(claveAcceso);
            if (!File.Exists(rutaXml))
            {
                await _xmlGenerator.GenerarXmlTemporalAsync(detalle.Preview);
                rutaXml = ConstruirLiquidacionXmlRutaLocal(claveAcceso);
            }
        }

        if (string.IsNullOrWhiteSpace(rutaXml) || !File.Exists(rutaXml))
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura,
                "No se pudo generar o ubicar el XML adjunto para enviar la liquidación de compra por correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = "No se pudo generar o ubicar el XML adjunto para enviar la liquidación de compra por correo."
            };
        }

        var rutaPdf = rutaPdfExistente;
        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
        {
            rutaPdf = ConstruirLiquidacionPdfRutaLocal(claveAcceso);
            if (!File.Exists(rutaPdf))
                rutaPdf = await _pdfService.GenerarPdfLiquidacionAsync(detalle.Preview);
        }

        if (string.IsNullOrWhiteSpace(rutaPdf) || !File.Exists(rutaPdf))
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura,
                "No se pudo generar o ubicar el PDF adjunto para enviar la liquidación de compra por correo.");

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = "No se pudo generar o ubicar el PDF adjunto para enviar la liquidación de compra por correo."
            };
        }

        try
        {
            var numeroLiquidacion = $"{detalle.Preview.SerieVisual}-{NormalizarSecuencial(detalle.Preview.Secuencial)}";
            await _emailService.EnviarLiquidacionCompraAsync(
                numeroLiquidacion,
                destinatarios,
                string.IsNullOrWhiteSpace(detalle.Preview.RazonSocialProveedor)
                    ? ObtenerNombreClienteLiquidacion(detalle.Proveedor)
                    : detalle.Preview.RazonSocialProveedor,
                detalle.Preview.ImporteTotal,
                rutaXml,
                rutaPdf);

            await _comprobanteCorreoEstadoService.MarcarEnviadoAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura);

            return new FacturaCorreoEnvioResultadoDto
            {
                Enviado = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"Se envió el correo con XML y PDF de la liquidación de compra a {destinatarios.Count} destinatario(s)."
            };
        }
        catch (Exception ex)
        {
            await _comprobanteCorreoEstadoService.MarcarErrorAsync(
                ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                codFactura,
                ex.Message);

            return new FacturaCorreoEnvioResultadoDto
            {
                Error = true,
                TotalDestinatarios = destinatarios.Count,
                Mensaje = $"No se pudo enviar el correo de la liquidación de compra: {ex.Message}"
            };
        }
    }

    public async Task<List<int>> GetLiquidacionesAutorizadasPendientesCorreoAsync(int maxRegistros = 20)
    {
        var idsPendientes = await _comprobanteCorreoEstadoService.GetDocumentosPendientesAsync(
            ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
            Math.Max(maxRegistros * 5, maxRegistros));

        await using var context = await _dbFactory.CreateDbContextAsync();

        if (!idsPendientes.Any())
        {
            var candidatasFallback = await context.ComprasFacturas
                .AsNoTracking()
                .Where(c =>
                    c.CodDocumento == "03" &&
                    c.Estado == true &&
                    c.Usuario != null)
                .OrderByDescending(c => c.CodFactura)
                .Select(c => new
                {
                    c.CodFactura,
                    c.Autorizado,
                    c.EstadoEnvioSRI
                })
                .Take(Math.Max(maxRegistros * 3, maxRegistros))
                .ToListAsync();

            var pendientesFallback = candidatasFallback
                .Where(c => LiquidacionCompraEstaAutorizada(c.Autorizado, c.EstadoEnvioSRI))
                .Select(c => c.CodFactura)
                .Distinct()
                .ToList();

            foreach (var documentoId in pendientesFallback)
            {
                await _comprobanteCorreoEstadoService.RegistrarPendienteAsync(
                    ComprobanteCorreoEstadoService.TipoLiquidacionCompra,
                    documentoId);
            }

            idsPendientes = pendientesFallback;
        }

        if (!idsPendientes.Any())
            return new List<int>();

        var candidatos = new List<(int CodFactura, string? Autorizado, string? EstadoEnvioSRI)>();
        foreach (var loteIds in idsPendientes
                     .Distinct()
                     .Chunk(10))
        {
            var idsLote = loteIds.ToList();
            var lote = await context.ComprasFacturas
                .AsNoTracking()
                .Where(c =>
                    idsLote.Contains(c.CodFactura) &&
                    c.CodDocumento == "03" &&
                    c.Estado == true)
                .Select(c => new
                {
                    c.CodFactura,
                    c.Autorizado,
                    c.EstadoEnvioSRI
                })
                .ToListAsync();

            candidatos.AddRange(lote.Select(c => (c.CodFactura, c.Autorizado, c.EstadoEnvioSRI)));
        }

        return candidatos
            .Where(c => LiquidacionCompraEstaAutorizada(c.Autorizado, c.EstadoEnvioSRI))
            .OrderBy(c => c.CodFactura)
            .Take(maxRegistros)
            .Select(c => c.CodFactura)
            .ToList();
    }

    public string GenerarClaveAccesoTemporal(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            throw new ArgumentNullException(nameof(preview));

        var fecha = (preview.FechaEmision ?? DateTime.Today).ToString("ddMMyyyy");
        var ruc = (preview.RucEmisor ?? "").Trim().PadLeft(13, '0');
        var ambiente = "2";
        var estab = (preview.Estab ?? "001").PadLeft(3, '0');
        var ptoEmi = (preview.PtoEmi ?? "001").PadLeft(3, '0');
        var secuencial = NormalizarSecuencial(preview.Secuencial);
        var codigoNumerico = "12345678";

        var baseClave = $"{fecha}03{ruc}{ambiente}{estab}{ptoEmi}{secuencial}{codigoNumerico}1";
        var verificador = CalcularModulo11(baseClave);
        return $"{baseClave}{verificador}";
    }

    public string ObtenerEtiquetaImpuesto(int codigoPorcentaje)
    {
        return codigoPorcentaje switch
        {
            5 => "IVA 5%",
            8 => "IVA 8%",
            4 => "IVA 15%",
            6 => "No objeto de IVA",
            7 => "Exento de IVA",
            _ => "IVA 0%"
        };
    }

    private async Task<LiquidacionCompraDetalleViewDto?> ConstruirDetalleLiquidacionAsync(ComprasFactura compra, AppDbContext context)
    {
        var proveedor = compra.CodClientes.HasValue
            ? await context.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Codcliente == compra.CodClientes.Value)
            : null;

        var emisor = compra.CodEmisor.HasValue
            ? await context.Emisores.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == compra.CodEmisor.Value)
            : null;

        var detallesDb = await context.ComprasDetalleFac
            .AsNoTracking()
            .Where(x => x.CodFactura == compra.CodFactura)
            .OrderBy(x => x.CodLinea)
            .ToListAsync();

        var preview = await ConstruirPreviewDesdeCompraAsync(compra, proveedor, emisor, detallesDb, context);

        return new LiquidacionCompraDetalleViewDto
        {
            CodFactura = compra.CodFactura,
            Compra = compra,
            Proveedor = proveedor,
            Emisor = emisor,
            Preview = preview,
            XmlUrl = ConstruirLiquidacionXmlUrl(preview.ClaveAcceso),
            PdfUrl = ConstruirLiquidacionPdfUrl(preview.ClaveAcceso)
        };
    }

    private async Task<string?> AsegurarRutaXmlLiquidacionAsync(int codFactura, LiquidacionCompraDetalleViewDto detalle)
    {
        var claveAcceso = detalle.Preview.ClaveAcceso;
        if (string.IsNullOrWhiteSpace(claveAcceso))
        {
            claveAcceso = GenerarClaveAccesoTemporal(detalle.Preview);
            detalle.Preview.ClaveAcceso = claveAcceso;
        }

        var rutaXml = ConstruirLiquidacionXmlRutaLocal(claveAcceso);
        if (!File.Exists(rutaXml))
        {
            await _xmlGenerator.GenerarXmlTemporalAsync(detalle.Preview);
            rutaXml = ConstruirLiquidacionXmlRutaLocal(claveAcceso);
        }

        if (File.Exists(rutaXml))
            return rutaXml;

        return null;
    }

    private async Task<LiquidacionCompraPreviewDto> ConstruirPreviewDesdeCompraAsync(
        ComprasFactura compra,
        Cliente? proveedor,
        Emisor? emisor,
        IReadOnlyCollection<ComprasDetalleFac> detallesDb,
        AppDbContext context)
    {
        var serie = compra.Serie ?? "";
        var (estab, ptoEmi) = SepararSerie(serie);

        var preview = new LiquidacionCompraPreviewDto
        {
            Usuario = compra.Usuario,
            ClaveAcceso = compra.CodClave ?? "",
            NumeroAutorizacion = compra.NumAutorizacion ?? compra.CodClave ?? "",
            EstaAutorizada = LiquidacionCompraEstaAutorizada(compra.Autorizado, compra.EstadoEnvioSRI),
            Ambiente = compra.Ambiente ?? (int.TryParse(emisor?.TipoAmbiente, out var ambienteEmisor) ? ambienteEmisor : 2),
            Estab = estab,
            PtoEmi = ptoEmi,
            Secuencial = NormalizarSecuencial(compra.NumFactura),
            FechaEmision = compra.FchAutorizacion ?? compra.FechaRegistro ?? DateTime.Today,
            CodEmisor = emisor?.Codigo ?? compra.CodEmisor,
            NombreEmisorEncontrado = emisor == null
                ? ""
                : $"{(string.IsNullOrWhiteSpace(emisor.NomComercial) ? emisor.RazonSocial : emisor.NomComercial)} - {emisor.Ruc}",
            RucEmisor = emisor?.Ruc?.Trim() ?? "",
            RazonSocialEmisor = emisor?.RazonSocial?.Trim() ?? "",
            NombreComercialEmisor = string.IsNullOrWhiteSpace(emisor?.NomComercial)
                ? (emisor?.RazonSocial?.Trim() ?? "")
                : emisor.NomComercial!.Trim(),
            DireccionMatriz = emisor?.DireccionMatriz?.Trim()
                ?? emisor?.DirEstablecimiento?.Trim()
                ?? "",
            DireccionEstablecimiento = emisor?.DirEstablecimiento?.Trim()
                ?? emisor?.DireccionMatriz?.Trim()
                ?? "",
            ContribuyenteEspecial = emisor?.ContribuyenteEspecial?.Trim() ?? "",
            ObligadoContabilidad = string.IsNullOrWhiteSpace(emisor?.LlevaContabilidad)
                ? "NO"
                : emisor!.LlevaContabilidad!.Trim().ToUpperInvariant(),
            EmailEmisor = emisor?.Email?.Trim() ?? "",
            TelefonoEmisor = emisor?.Telefono?.Trim() ?? "",
            LogoEmisor = emisor?.LogoImagen?.Trim() ?? "",
            TipoIdentificacionProveedor = proveedor?.Tipoidentificacion ?? "05",
            IdentificacionProveedor = proveedor?.Numeroidentificacion ?? "",
            RazonSocialProveedor = ObtenerNombreClienteLiquidacion(proveedor),
            DireccionProveedor = proveedor?.Direccion ?? "",
            TelefonoFijoProveedor = proveedor?.Telefonoconvencional ?? "",
            TelefonoProveedor = proveedor?.Celular ?? "",
            EmailProveedor = proveedor?.Correo ?? "",
            CodProveedor = proveedor?.Codcliente,
            FormaPago = string.IsNullOrWhiteSpace(compra.TipoPago) ? "20" : compra.TipoPago!,
            Plazo = compra.TiempoCredito,
            UnidadTiempo = (compra.TiempoCredito ?? 0) > 0 ? "dias" : "dias",
            Moneda = ObtenerMonedaLiquidacion(detallesDb),
            Subtotal0 = compra.Subtotal0 ?? compra.SubCeroTotal ?? 0m,
            Subtotal5 = compra.Subtotal5 ?? 0m,
            Subtotal8 = compra.Subtotal8 ?? 0m,
            Subtotal15 = compra.Subtotal12 ?? compra.SubDoceTotal ?? 0m,
            NoImp = compra.NoImp ?? compra.SubNoImpTotal ?? 0m,
            ExIva = compra.ExIva ?? compra.SubExIvaTotal ?? 0m,
            TotalSinImpuestos = compra.Subtotal ?? 0m,
            TotalDescuento = compra.Descuentos ?? 0m,
            Iva5 = compra.Iva5 ?? 0m,
            Iva8 = compra.Iva8 ?? 0m,
            Iva15 = Red2((compra.Iva ?? 0m) - (compra.Iva5 ?? 0m) - (compra.Iva8 ?? 0m)),
            IvaTotal = compra.Iva ?? 0m,
            ImporteTotal = compra.ValorTotal ?? 0m,
            Detalles = detallesDb.Select(detalle => new LiquidacionCompraDetalleDto
            {
                CodProducto = detalle.CodProducto,
                CodPrincipal = detalle.CodPrincipal ?? "",
                CodAuxiliar = detalle.CodAuxiliar ?? "",
                Descripcion = detalle.DescripProducto ?? "",
                Cantidad = detalle.CantProducto ?? 0m,
                PrecioUnitario = detalle.PrecioProducto ?? 0m,
                Descuento = detalle.Descuento ?? 0m,
                PrecioTotalSinImpuesto = detalle.ValorTProducto ?? 0m,
                CodigoPorcentaje = detalle.PorImp ?? 0,
                Tarifa = detalle.Tarifa ?? 0,
                ValorIva = detalle.ValorIVA ?? 0m,
                ValorTotal = detalle.ValorTotal ?? 0m
            }).ToList()
        };

        preview.TipoIdentificacionProveedorNombre =
            await ObtenerDescripcionTipoIdentificacionAsync(preview.TipoIdentificacionProveedor, context);

        preview.FormaPagoNombre =
            await ObtenerDescripcionFormaPagoAsync(preview.FormaPago, context);

        if (string.IsNullOrWhiteSpace(preview.ClaveAcceso))
            preview.ClaveAcceso = GenerarClaveAccesoTemporal(preview);

        if (string.IsNullOrWhiteSpace(preview.NumeroAutorizacion))
            preview.NumeroAutorizacion = preview.ClaveAcceso;

        if (preview.Detalles.Count == 0)
            preview.Detalles.Add(CrearDetalleVacio());

        RecalcularTotalesDesdeDetalles(preview);
        return preview;
    }

    private async Task<Emisor?> BuscarEmisorAsync(LiquidacionCompraPreviewDto preview, AppDbContext context)
    {
        Emisor? emisor = null;
        var usuarioEmisor = await ResolveEmisorOwnerUserIdAsync(context, preview.Usuario);

        if (preview.CodEmisor.HasValue && preview.CodEmisor.Value > 0)
        {
            emisor = await context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Codigo == preview.CodEmisor.Value &&
                    (!usuarioEmisor.HasValue || x.IdUsuario == usuarioEmisor.Value));
        }

        if (emisor == null && !string.IsNullOrWhiteSpace(preview.RucEmisor))
        {
            emisor = await context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Ruc == preview.RucEmisor &&
                    (!usuarioEmisor.HasValue || x.IdUsuario == usuarioEmisor.Value));
        }

        if (emisor == null && usuarioEmisor.HasValue)
        {
            emisor = await context.Emisores
                .AsNoTracking()
                .Where(x => x.Estado && x.IdUsuario == usuarioEmisor.Value)
                .OrderByDescending(x => x.Codigo)
                .FirstOrDefaultAsync();
        }

        if (emisor == null)
        {
            emisor = await context.Emisores
                .AsNoTracking()
                .Where(x => x.Estado)
                .OrderByDescending(x => x.Codigo)
                .FirstOrDefaultAsync();
        }

        return emisor;
    }

    private static async Task<int?> ResolveEmisorOwnerUserIdAsync(AppDbContext context, int? idUsuario)
    {
        if (!idUsuario.HasValue || idUsuario.Value <= 0)
            return idUsuario;

        var usuario = await context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario.Value);

        if (usuario == null)
            return idUsuario;

        return usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario.Value;
    }

    private async Task ResolverProveedorAsync(LiquidacionCompraPreviewDto preview, AppDbContext context)
    {
        preview.CodProveedor = null;

        if (string.IsNullOrWhiteSpace(preview.IdentificacionProveedor))
            return;

        var identificacion = preview.IdentificacionProveedor.Trim();

        var cliente = await context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Numeroidentificacion == identificacion);

        if (cliente != null)
        {
            preview.CodProveedor = cliente.Codcliente;

            if (string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
            {
                preview.RazonSocialProveedor =
                    cliente.Nombrerazonsocial?.Trim()
                    ?? cliente.Nombrecomercial?.Trim()
                    ?? ((cliente.Nombres ?? "") + " " + (cliente.Apellidos ?? "")).Trim();
            }

            if (string.IsNullOrWhiteSpace(preview.DireccionProveedor))
                preview.DireccionProveedor = cliente.Direccion?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor))
                preview.TelefonoFijoProveedor = cliente.Telefonoconvencional?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
                preview.TelefonoProveedor = cliente.Celular?.Trim()
                    ?? cliente.Telefonoconvencional?.Trim()
                    ?? "";

            if (string.IsNullOrWhiteSpace(preview.EmailProveedor))
                preview.EmailProveedor = cliente.Correo?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedor) && !string.IsNullOrWhiteSpace(cliente.Tipoidentificacion))
                preview.TipoIdentificacionProveedor = cliente.Tipoidentificacion!.Trim();
        }

        var proveedor = await context.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ruc == identificacion);

        if (proveedor == null)
            return;

        if (string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
            preview.RazonSocialProveedor = proveedor.nombre?.Trim() ?? proveedor.nombreComercial?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.DireccionProveedor))
            preview.DireccionProveedor = proveedor.direccion?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor))
            preview.TelefonoFijoProveedor = proveedor.telefono?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
            preview.TelefonoProveedor = proveedor.telefonoMovil?.Trim() ?? proveedor.telefono?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.EmailProveedor))
            preview.EmailProveedor = proveedor.email?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedor) && !string.IsNullOrWhiteSpace(proveedor.tipoIdentificacion))
            preview.TipoIdentificacionProveedor = proveedor.tipoIdentificacion!.Trim();
    }

    private async Task<string> ObtenerSerieComprasAsync(int? usuario, Emisor? emisor, AppDbContext context)
    {
        if (usuario.HasValue && usuario.Value > 0)
        {
            var resolucion = await ResolverSerieLiquidacionAsync(usuario.Value);
            return resolucion.SerieVisual;
        }

        if (emisor != null &&
            !string.IsNullOrWhiteSpace(emisor.CodEstablecimiento) &&
            !string.IsNullOrWhiteSpace(emisor.CodPuntoEmision))
        {
            return $"{emisor.CodEstablecimiento!.Trim()}-{emisor.CodPuntoEmision!.Trim()}";
        }

        return "001-001";
    }

    private async Task<string> GenerarSiguienteSecuencialAsync(int? codEmisor, string serie, AppDbContext context)
    {
        var serieNormalizada = (serie ?? "").Trim();

        var query = context.ComprasFacturas
            .AsNoTracking()
            .Where(x =>
                x.Estado == true &&
                x.CodDocumento == "03" &&
                (x.Serie ?? "") == serieNormalizada);

        if (codEmisor.HasValue)
            query = query.Where(x => x.CodEmisor == codEmisor.Value);

        if (codEmisor.HasValue)
        {
            var usuarioEmisor = await context.Emisores
                .AsNoTracking()
                .Where(x => x.Codigo == codEmisor.Value)
                .Select(x => x.IdUsuario)
                .FirstOrDefaultAsync();

            if (usuarioEmisor is > 0)
            {
                var usuario = await context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.IdUsuario == usuarioEmisor.Value)
                    .Select(u => new { u.IdUsuario, u.idJefe, u.estadoAsociado })
                    .FirstOrDefaultAsync();

                var titularId = usuario?.estadoAsociado == true && usuario.idJefe is > 0
                    ? usuario.idJefe.Value
                    : usuarioEmisor.Value;

                var usuariosCuenta = await context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.IdUsuario == titularId || (u.idJefe == titularId && u.estadoAsociado == true))
                    .Select(u => u.IdUsuario)
                    .ToListAsync();

                if (usuariosCuenta.Count > 0)
                    query = query.Where(x => x.Usuario.HasValue && usuariosCuenta.Contains(x.Usuario.Value));
            }
        }

        var posibles = await query
            .Select(x => x.NumFactura)
            .ToListAsync();

        var maximo = 0;
        foreach (var valor in posibles)
        {
            var soloNumero = new string((valor ?? "").Where(char.IsDigit).ToArray());
            if (int.TryParse(soloNumero, out var numero) && numero > maximo)
                maximo = numero;
        }

        return (maximo + 1).ToString("D9", CultureInfo.InvariantCulture);
    }

    private void ValidarLiquidacion(LiquidacionCompraPreviewDto preview)
    {
        if (!preview.ExisteEmisor)
            throw new Exception("Debes tener un emisor configurado para registrar la liquidacion.");

        if (!EsCodigoTresDigitos(preview.Estab) || !EsCodigoTresDigitos(preview.PtoEmi))
            throw new Exception("El establecimiento y punto de emision deben tener 3 digitos validos.");

        if (!EsSecuencialValido(preview.Secuencial))
            throw new Exception("Debes ingresar un secuencial valido de 9 digitos.");

        if (!preview.FechaEmision.HasValue)
            throw new Exception("Debes ingresar la fecha de emision.");

        if (preview.FechaEmision.Value.Date > DateTime.Today)
            throw new Exception("La fecha de emision no puede ser futura.");

        // if (!ValidarRucEcuatoriano((preview.RucEmisor ?? "").Trim()))
        //     throw new Exception("El RUC del emisor no es valido.");

        preview.DireccionEstablecimiento = (preview.DireccionEstablecimiento ?? "").Trim();
        preview.DireccionMatriz = preview.DireccionEstablecimiento;

        if (!TieneLongitudMinima(preview.DireccionEstablecimiento, 5))
            throw new Exception("Debes ingresar la direccion del emisor.");

        if (preview.Ambiente is not 1 and not 2)
            throw new Exception("Debes seleccionar un ambiente valido.");

        if (string.IsNullOrWhiteSpace(preview.FormaPago))
            throw new Exception("Debes seleccionar una forma de pago.");

        if ((preview.Plazo ?? 0) > 0 && !TieneLongitudMinima(preview.UnidadTiempo, 3))
            throw new Exception("Debes ingresar la unidad de tiempo cuando existe plazo.");

        if (!TieneLongitudMinima(preview.Moneda, 3))
            throw new Exception("Debes ingresar la moneda de la liquidacion.");

        if (string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedor))
            throw new Exception("Debes seleccionar el tipo de identificacion del proveedor.");

        if (preview.TipoIdentificacionProveedor == "07")
            throw new Exception("La liquidacion de compra no admite consumidor final como tipo de identificacion.");

        if (string.IsNullOrWhiteSpace(preview.IdentificacionProveedor))
            throw new Exception("Debes ingresar la identificacion del proveedor.");

        if (!ValidarIdentificacionProveedor(preview.TipoIdentificacionProveedor, preview.IdentificacionProveedor))
            throw new Exception("La identificacion del proveedor no es valida para el tipo seleccionado.");

        if (string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
            throw new Exception("Debes ingresar la razon social o nombres del proveedor.");

        if (!TieneLongitudMinima(preview.DireccionProveedor, 5))
            throw new Exception("Debes ingresar la direccion del proveedor.");



        if (!string.IsNullOrWhiteSpace(preview.EmailProveedor) && !EsCorreoValido(preview.EmailProveedor))
            throw new Exception("El correo del proveedor no tiene un formato valido.");

        preview.CorreosAdicionalesProveedor = ComprobanteCorreoDestinatariosHelper
            .NormalizarCorreos(preview.CorreosAdicionalesProveedor)
            .Where(c => !string.Equals(c, preview.EmailProveedor?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
        preview.CorreosAdicionalesProveedorGuardar = ComprobanteCorreoDestinatariosHelper
            .NormalizarCorreos(preview.CorreosAdicionalesProveedorGuardar)
            .Where(c => preview.CorreosAdicionalesProveedor.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        foreach (var correo in preview.CorreosAdicionalesProveedor)
        {
            if (!EsCorreoValido(correo))
                throw new Exception($"El correo adicional {correo} no tiene un formato valido.");
        }

        preview.Detalles ??= new List<LiquidacionCompraDetalleDto>();
        if (!preview.Detalles.Any())
            throw new Exception("Debes agregar al menos un detalle.");

        foreach (var item in preview.Detalles)
        {
            if (string.IsNullOrWhiteSpace(item.Descripcion))
                throw new Exception("Todos los detalles deben tener descripcion.");

            if (item.Cantidad <= 0m)
                throw new Exception("La cantidad de cada detalle debe ser mayor a cero.");

            if (item.PrecioUnitario <= 0m)
                throw new Exception("El precio unitario debe ser mayor a cero.");

            if (item.Descuento < 0m)
                throw new Exception("El descuento no puede ser negativo.");

            if (item.Descuento > (item.Cantidad * item.PrecioUnitario))
                throw new Exception("El descuento no puede superar el subtotal bruto del detalle.");
        }

        if (preview.ImporteTotal <= 0m)
            throw new Exception("El total de la liquidacion debe ser mayor a cero.");
    }

    private async Task<Cliente?> ObtenerOCrearClienteProveedorAsync(LiquidacionCompraPreviewDto preview, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(preview.IdentificacionProveedor))
            return null;

        var identificacion = preview.IdentificacionProveedor.Trim();
        var usuarioProveedor = await ResolveEmisorOwnerUserIdAsync(context, preview.Usuario);

        var cliente = usuarioProveedor.HasValue
            ? await context.Clientes
                .FirstOrDefaultAsync(x =>
                    x.Numeroidentificacion == identificacion &&
                    x.Usuario == usuarioProveedor.Value)
            : null;

        cliente ??= await context.Clientes
            .FirstOrDefaultAsync(x =>
                x.Numeroidentificacion == identificacion &&
                x.Usuario == null);

        if (cliente == null)
        {
            cliente = new Cliente
            {
                Numeroidentificacion = identificacion,
                Tipoidentificacion = preview.TipoIdentificacionProveedor,
                Nombrerazonsocial = preview.RazonSocialProveedor,
                Nombrecomercial = preview.RazonSocialProveedor,
                Nombres = preview.RazonSocialProveedor,
                Apellidos = ".",
                Direccion = NormalizarTextoVacio(preview.DireccionProveedor),
                Telefonoconvencional = LimpiarTelefono(ObtenerTelefonoFijo(preview)),
                Celular = LimpiarTelefono(ObtenerTelefonoMovil(preview)),
                Correo = NormalizarTextoVacio(preview.EmailProveedor),
                TipoCliente = 2,
                Usuario = usuarioProveedor ?? preview.Usuario,
                Estado = true
            };

            context.Clientes.Add(cliente);
            await context.SaveChangesAsync();
            preview.CodProveedor = cliente.Codcliente;
            return cliente;
        }

        var cambio = false;

        if (!cliente.Usuario.HasValue && (usuarioProveedor ?? preview.Usuario).HasValue)
        {
            cliente.Usuario = usuarioProveedor ?? preview.Usuario;
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedor) &&
            cliente.Tipoidentificacion != preview.TipoIdentificacionProveedor)
        {
            cliente.Tipoidentificacion = preview.TipoIdentificacionProveedor;
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
        {
            if (cliente.Nombrerazonsocial != preview.RazonSocialProveedor)
            {
                cliente.Nombrerazonsocial = preview.RazonSocialProveedor;
                cambio = true;
            }

            if (cliente.Nombrecomercial != preview.RazonSocialProveedor)
            {
                cliente.Nombrecomercial = preview.RazonSocialProveedor;
                cambio = true;
            }

            if (cliente.Nombres != preview.RazonSocialProveedor)
            {
                cliente.Nombres = preview.RazonSocialProveedor;
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.DireccionProveedor) &&
            cliente.Direccion != preview.DireccionProveedor.Trim())
        {
            cliente.Direccion = preview.DireccionProveedor.Trim();
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor))
        {
            if (cliente.Telefonoconvencional != preview.TelefonoFijoProveedor.Trim())
            {
                cliente.Telefonoconvencional = preview.TelefonoFijoProveedor.Trim();
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
        {
            if (cliente.Celular != preview.TelefonoProveedor.Trim())
            {
                cliente.Celular = preview.TelefonoProveedor.Trim();
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.EmailProveedor) &&
            cliente.Correo != preview.EmailProveedor.Trim())
        {
            cliente.Correo = preview.EmailProveedor.Trim();
            cambio = true;
        }

        if (cambio)
            await context.SaveChangesAsync();

        preview.CodProveedor = cliente.Codcliente;
        return cliente;
    }

    private static async Task SincronizarCorreosAdicionalesProveedorAsync(
        LiquidacionCompraPreviewDto preview,
        Cliente? cliente,
        AppDbContext context)
    {
        if (cliente == null)
            return;

        var correos = ComprobanteCorreoDestinatariosHelper
            .NormalizarCorreos(preview.CorreosAdicionalesProveedorGuardar)
            .Where(c => !string.Equals(c, preview.EmailProveedor?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        if (!correos.Any())
            return;

        var existentes = await context.ClientesCorreos
            .Where(x => x.CodCliente == cliente.Codcliente)
            .ToListAsync();

        foreach (var correo in correos)
        {
            var existente = existentes.FirstOrDefault(x =>
                string.Equals(x.Correo, correo, StringComparison.OrdinalIgnoreCase));

            if (existente == null)
            {
                context.ClientesCorreos.Add(new ClienteCorreo
                {
                    CodCliente = cliente.Codcliente,
                    Correo = correo,
                    Estado = true
                });
            }
            else if (!existente.Estado)
            {
                existente.Estado = true;
            }
        }

        await context.SaveChangesAsync();
    }

    private async Task<Proveedor?> ObtenerOCrearProveedorAsync(LiquidacionCompraPreviewDto preview, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(preview.IdentificacionProveedor))
            return null;

        var identificacion = preview.IdentificacionProveedor.Trim();

        var proveedor = await context.Proveedores
            .FirstOrDefaultAsync(x => x.ruc == identificacion);

        if (proveedor == null)
        {
            proveedor = new Proveedor
            {
                ruc = identificacion,
                nombre = preview.RazonSocialProveedor,
                nombreComercial = preview.RazonSocialProveedor,
                direccion = NormalizarTextoVacio(preview.DireccionProveedor),
                telefono = LimpiarTelefono(ObtenerTelefonoFijo(preview)),
                telefonoMovil = LimpiarTelefono(ObtenerTelefonoMovil(preview)),
                email = NormalizarTextoVacio(preview.EmailProveedor),
                estado = true,
                fechaActualizacion = DateTime.Now,
                tipoIdentificacion = preview.TipoIdentificacionProveedor,
                personaNatural = '0',
                formaPago = string.IsNullOrWhiteSpace(preview.FormaPago) ? "20" : preview.FormaPago,
                plazoPago = preview.Plazo ?? 0,
                saldoInicial = 0m,
                cuentaContable = NormalizarTextoVacio(preview.CuentaContableProveedor),
                llevaRetencion = preview.EsSujetoRetencionProveedor,
                tipoCuenta = NormalizarTextoVacio(preview.TipoCuentaProveedor),
                numeroCuenta = NormalizarTextoVacio(preview.NumeroCuentaProveedor),
                institucionFin = ObtenerCodigoEntero(preview.BancoProveedor)
            };

            context.Proveedores.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor;
        }

        var cambio = false;

        if (!string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
        {
            if (proveedor.nombre != preview.RazonSocialProveedor)
            {
                proveedor.nombre = preview.RazonSocialProveedor;
                cambio = true;
            }

            if (proveedor.nombreComercial != preview.RazonSocialProveedor)
            {
                proveedor.nombreComercial = preview.RazonSocialProveedor;
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.DireccionProveedor) &&
            proveedor.direccion != preview.DireccionProveedor.Trim())
        {
            proveedor.direccion = preview.DireccionProveedor.Trim();
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor))
        {
            if (proveedor.telefono != preview.TelefonoFijoProveedor.Trim())
            {
                proveedor.telefono = preview.TelefonoFijoProveedor.Trim();
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
        {
            if (proveedor.telefonoMovil != preview.TelefonoProveedor.Trim())
            {
                proveedor.telefonoMovil = preview.TelefonoProveedor.Trim();
                cambio = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(preview.EmailProveedor) &&
            proveedor.email != preview.EmailProveedor.Trim())
        {
            proveedor.email = preview.EmailProveedor.Trim();
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.TipoIdentificacionProveedor) &&
            proveedor.tipoIdentificacion != preview.TipoIdentificacionProveedor)
        {
            proveedor.tipoIdentificacion = preview.TipoIdentificacionProveedor;
            cambio = true;
        }

        var formaPago = string.IsNullOrWhiteSpace(preview.FormaPago) ? "20" : preview.FormaPago;
        if (proveedor.formaPago != formaPago)
        {
            proveedor.formaPago = formaPago;
            cambio = true;
        }

        var plazo = preview.Plazo ?? 0;
        if (proveedor.plazoPago != plazo)
        {
            proveedor.plazoPago = plazo;
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.CuentaContableProveedor) &&
            proveedor.cuentaContable != preview.CuentaContableProveedor.Trim())
        {
            proveedor.cuentaContable = preview.CuentaContableProveedor.Trim();
            cambio = true;
        }

        if (proveedor.llevaRetencion != preview.EsSujetoRetencionProveedor)
        {
            proveedor.llevaRetencion = preview.EsSujetoRetencionProveedor;
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.TipoCuentaProveedor) &&
            proveedor.tipoCuenta != preview.TipoCuentaProveedor.Trim())
        {
            proveedor.tipoCuenta = preview.TipoCuentaProveedor.Trim();
            cambio = true;
        }

        if (!string.IsNullOrWhiteSpace(preview.NumeroCuentaProveedor) &&
            proveedor.numeroCuenta != preview.NumeroCuentaProveedor.Trim())
        {
            proveedor.numeroCuenta = preview.NumeroCuentaProveedor.Trim();
            cambio = true;
        }

        var bancoCodigo = ObtenerCodigoEntero(preview.BancoProveedor);
        if (bancoCodigo.HasValue && proveedor.institucionFin != bancoCodigo)
        {
            proveedor.institucionFin = bancoCodigo;
            cambio = true;
        }

        if (cambio)
        {
            proveedor.fechaActualizacion = DateTime.Now;
            await context.SaveChangesAsync();
        }

        return proveedor;
    }

    private async Task<string> ObtenerDescripcionFormaPagoAsync(string? codigo, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return "";

        var forma = await context.FormasPago
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo == codigo && x.Estado == true);

        if (forma == null)
            return codigo;

        return !string.IsNullOrWhiteSpace(forma.DescripcionSri)
            ? forma.DescripcionSri!
            : (forma.Descripcion ?? codigo);
    }

    private async Task<string> ObtenerDescripcionTipoIdentificacionAsync(string? codigo, AppDbContext context)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return "";

        var tipo = await context.Identificacion
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdeCodigo == codigo && x.Estado == true);

        return tipo?.IdeDescripcion ?? codigo;
    }

    private static bool DebeSobrescribirSerie(string? valor)
    {
        var texto = (valor ?? "").Trim();
        return string.IsNullOrWhiteSpace(texto) || texto == "000" || texto == "001";
    }

    private static bool EsDetalleVacio(LiquidacionCompraDetalleDto item)
    {
        return item != null &&
               string.IsNullOrWhiteSpace(item.CodPrincipal) &&
               string.IsNullOrWhiteSpace(item.CodAuxiliar) &&
               string.IsNullOrWhiteSpace(item.Descripcion) &&
               item.PrecioUnitario <= 0m &&
               item.PorcentajeDescuento <= 0m &&
               item.Descuento <= 0m;
    }

    private static (string estab, string ptoEmi) SepararSerie(string serie)
    {
        serie = (serie ?? "").Trim().Replace("-", "");

        if (serie.Length >= 6)
            return (serie.Substring(0, 3), serie.Substring(3, 3));

        if (serie.Length == 3)
            return (serie, "001");

        return ("001", "001");
    }

    private static string NormalizarSecuencial(string? secuencial)
    {
        var soloNumero = new string((secuencial ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(soloNumero))
            soloNumero = "1";

        if (soloNumero.Length > 9)
            soloNumero = soloNumero[^9..];

        return soloNumero.PadLeft(9, '0');
    }

    private static string NormalizarTextoVacio(string? valor, string defecto = ".")
    {
        return string.IsNullOrWhiteSpace(valor) ? defecto : valor.Trim();
    }

    private static int? ObtenerCodigoEntero(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto = valor.Trim();
        var token = texto.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(token, out var codigo) ? codigo : null;
    }

    private static LiquidacionProveedorLookupDto CombinarProveedorLookup(IEnumerable<LiquidacionProveedorLookupDto> items)
    {
        var lista = items.ToList();
        var baseItem = lista.First();

        string Tomar(params string?[] valores) =>
            valores.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        return new LiquidacionProveedorLookupDto
        {
            CodCliente = lista.Select(x => x.CodCliente).FirstOrDefault(x => x.HasValue),
            Identificacion = Tomar(lista.Select(x => x.Identificacion).ToArray()),
            TipoIdentificacion = Tomar(lista.Select(x => x.TipoIdentificacion).ToArray()),
            RazonSocial = Tomar(lista.Select(x => x.RazonSocial).ToArray()),
            Direccion = Tomar(lista.Select(x => x.Direccion).ToArray()),
            TelefonoFijo = Tomar(lista.Select(x => x.TelefonoFijo).ToArray()),
            TelefonoMovil = Tomar(lista.Select(x => x.TelefonoMovil).ToArray()),
            Correo = Tomar(lista.Select(x => x.Correo).ToArray())
        };
    }

    private static bool EmpiezaCon(string? valor, string filtro)
    {
        return !string.IsNullOrWhiteSpace(valor) &&
               valor.Trim().StartsWith(filtro.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? CalcularFechaVence(DateTime? fechaEmision, int? plazo)
    {
        if (!fechaEmision.HasValue)
            return null;

        return fechaEmision.Value.Date.AddDays(Math.Max(plazo ?? 0, 0));
    }

    private static string ObtenerTelefonoFijo(LiquidacionCompraPreviewDto preview)
    {
        return !string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor)
            ? preview.TelefonoFijoProveedor
            : preview.TelefonoProveedor;
    }

    private static string ObtenerTelefonoMovil(LiquidacionCompraPreviewDto preview)
    {
        return !string.IsNullOrWhiteSpace(preview.TelefonoProveedor)
            ? preview.TelefonoProveedor
            : preview.TelefonoFijoProveedor;
    }

    private static string LimpiarTelefono(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? "." : valor.Trim();
    }

    private static bool TieneLongitudMinima(string? valor, int minimo)
    {
        return !string.IsNullOrWhiteSpace(valor) && valor.Trim().Length >= minimo;
    }

    private static bool EsCorreoValido(string valor)
    {
        try
        {
            var email = new MailAddress(valor.Trim());
            return email.Address.Equals(valor.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool EsCodigoTresDigitos(string? valor)
    {
        var limpio = new string((valor ?? "").Where(char.IsDigit).ToArray());
        return limpio.Length == 3 && limpio != "000";
    }

    private static bool EsSecuencialValido(string? valor)
    {
        var limpio = new string((valor ?? "").Where(char.IsDigit).ToArray());
        return limpio.Length == 9 && limpio != "000000000";
    }

    private static bool ValidarIdentificacionProveedor(string? tipo, string? identificacion)
    {
        var valor = (identificacion ?? "").Trim();
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        return tipo switch
        {
            "04" => ValidarRucEcuatoriano(valor),
            "05" => ValidarCedulaEcuatoriana(valor),
            "06" => Regex.IsMatch(valor, "^[A-Za-z0-9\\-]{6,20}$"),
            "08" => valor.Length is >= 5 and <= 20,
            _ => valor.Length >= 3
        };
    }

    private static bool ValidarCedulaEcuatoriana(string cedula)
    {
        cedula = new string((cedula ?? "").Where(char.IsDigit).ToArray());

        if (cedula.Length != 10)
            return false;

        int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
        var provincia = int.Parse(cedula[..2], CultureInfo.InvariantCulture);
        var tercerDigito = int.Parse(cedula.Substring(2, 1), CultureInfo.InvariantCulture);

        if (provincia < 1 || provincia > 24 || tercerDigito > 5)
            return false;

        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var valor = int.Parse(cedula[i].ToString(), CultureInfo.InvariantCulture) * coeficientes[i];
            suma += valor > 9 ? valor - 9 : valor;
        }

        var digitoRecibido = int.Parse(cedula[9].ToString(), CultureInfo.InvariantCulture);
        var digitoCalculado = (10 - (suma % 10)) % 10;
        return digitoCalculado == digitoRecibido;
    }

    private static bool ValidarRucEcuatoriano(string ruc)
    {
        ruc = new string((ruc ?? "").Where(char.IsDigit).ToArray());

        if (ruc.Length != 13)
            return false;

        var provincia = int.Parse(ruc[..2], CultureInfo.InvariantCulture);
        if (provincia < 1 || provincia > 24)
            return false;

        var tercerDigito = int.Parse(ruc.Substring(2, 1), CultureInfo.InvariantCulture);

        return tercerDigito switch
        {
            <= 5 => ValidarCedulaEcuatoriana(ruc[..10]) && ruc.EndsWith("001", StringComparison.Ordinal),
            6 => ValidarRucPublico(ruc),
            9 => ValidarRucPrivado(ruc),
            _ => false
        };
    }

    private static bool ValidarRucPrivado(string ruc)
    {
        int[] coeficientes = { 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;

        for (var i = 0; i < coeficientes.Length; i++)
            suma += int.Parse(ruc[i].ToString(), CultureInfo.InvariantCulture) * coeficientes[i];

        var residuo = suma % 11;
        var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
        var digitoRecibido = int.Parse(ruc[9].ToString(), CultureInfo.InvariantCulture);

        return digitoCalculado == digitoRecibido && ruc[10..] != "000";
    }

    private static bool ValidarRucPublico(string ruc)
    {
        int[] coeficientes = { 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;

        for (var i = 0; i < coeficientes.Length; i++)
            suma += int.Parse(ruc[i].ToString(), CultureInfo.InvariantCulture) * coeficientes[i];

        var residuo = suma % 11;
        var digitoCalculado = residuo == 0 ? 0 : 11 - residuo;
        var digitoRecibido = int.Parse(ruc[8].ToString(), CultureInfo.InvariantCulture);

        return digitoCalculado == digitoRecibido && ruc[9..] != "0000";
    }

    private static string ObtenerNombreClienteLiquidacion(Cliente? cliente)
    {
        if (cliente == null)
            return "";

        if (!string.IsNullOrWhiteSpace(cliente.Nombrerazonsocial))
            return cliente.Nombrerazonsocial.Trim();

        var nombre = $"{cliente.Nombres} {cliente.Apellidos}".Trim();
        if (!string.IsNullOrWhiteSpace(nombre))
            return nombre;

        return cliente.Nombrecomercial?.Trim() ?? "";
    }

    private static string ObtenerMonedaLiquidacion(IEnumerable<ComprasDetalleFac> detalles)
    {
        foreach (var detalle in detalles)
        {
            var observacion = detalle.Observacion ?? "";
            var indice = observacion.IndexOf("Moneda:", StringComparison.OrdinalIgnoreCase);
            if (indice < 0)
                continue;

            var valor = observacion[(indice + "Moneda:".Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
        }

        return "DOLAR";
    }

    private static string ConstruirLiquidacionXmlUrl(string? claveAcceso)
    {
        if (string.IsNullOrWhiteSpace(claveAcceso))
            return "";

        return $"/comprobantes/liquidaciones/LIQ_{claveAcceso.Trim()}.xml";
    }

    private static string ConstruirLiquidacionPdfUrl(string? claveAcceso, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        if (string.IsNullOrWhiteSpace(claveAcceso))
            return "";

        return $"/comprobantes/liquidaciones/LIQ_{claveAcceso.Trim()}{formato.ObtenerSufijoArchivo()}.pdf";
    }

    private static string ConstruirLiquidacionXmlRutaLocal(string claveAcceso)
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "comprobantes",
            "liquidaciones",
            $"LIQ_{claveAcceso.Trim()}.xml");
    }

    private static string ConstruirLiquidacionPdfRutaLocal(string claveAcceso, FormatoImpresionDocumento formato = FormatoImpresionDocumento.A4)
    {
        return Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "comprobantes",
            "liquidaciones",
            $"LIQ_{claveAcceso.Trim()}{formato.ObtenerSufijoArchivo()}.pdf");
    }

    private static bool LiquidacionCompraEstaAutorizada(string? autorizado, string? estadoSri)
    {
        if (!string.IsNullOrWhiteSpace(autorizado))
        {
            var valor = autorizado.Trim();
            if (valor == "1"
                || valor.Equals("true", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("t", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("s", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("si", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("sí", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("a", StringComparison.OrdinalIgnoreCase)
                || valor.Equals("autorizado", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(estadoSri)
            && estadoSri.Contains("AUTORIZ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolverEstadoSriLiquidacion(string? autorizado, string? estadoActual)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return estadoActual;

        var valor = autorizado.Trim();
        if (valor is "0" or "1")
            return estadoActual;

        return valor;
    }

    private static string? ResolverBanderaAutorizacionLiquidacion(string? autorizado, string? valorActual)
    {
        if (string.IsNullOrWhiteSpace(autorizado))
            return valorActual;

        var valor = autorizado.Trim();
        return DocumentoAutorizacionHelper.EsEstadoAutorizado(valor) || DocumentoAutorizacionHelper.EsBanderaAutorizada(valor)
            ? "1"
            : "0";
    }

    private static decimal Red2(decimal valor)
    {
        return Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }

    private static int ObtenerTarifaDesdeCodigo(int codigoPorcentaje)
    {
        return codigoPorcentaje switch
        {
            5 => 5,
            8 => 8,
            4 => 15,
            _ => 0
        };
    }

    private static decimal CalcularValorIva(int codigoPorcentaje, decimal baseImponible)
    {
        return codigoPorcentaje switch
        {
            5 => baseImponible * 0.05m,
            8 => baseImponible * 0.08m,
            4 => baseImponible * 0.15m,
            _ => 0m
        };
    }

    private static int CalcularModulo11(string numero)
    {
        var suma = 0;
        var factor = 2;

        for (var i = numero.Length - 1; i >= 0; i--)
        {
            suma += (numero[i] - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var resultado = 11 - (suma % 11);

        if (resultado == 11) return 0;
        if (resultado == 10) return 1;
        return resultado;
    }
}
