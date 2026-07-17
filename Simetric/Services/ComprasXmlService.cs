using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs;
using Simetric.Models;
using System.Globalization;
using System.Xml.Linq;

namespace Simetric.Services;

public class ComprasXmlService
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ComprobanteRetencionGenerator _retencionXmlGenerator;
    private readonly IRetencionPdfService _retencionPdfService;
    private readonly RetencionGeneradaService _retencionGeneradaService;
    private readonly EmisionControlService _emisionControlService;

    public ComprasXmlService(
        AppDbContext context,
        IDbContextFactory<AppDbContext> dbFactory,
        ComprobanteRetencionGenerator retencionXmlGenerator,
        IRetencionPdfService retencionPdfService,
        RetencionGeneradaService retencionGeneradaService,
        EmisionControlService emisionControlService)
    {
        _context = context;
        _dbFactory = dbFactory;
        _retencionXmlGenerator = retencionXmlGenerator;
        _retencionPdfService = retencionPdfService;
        _retencionGeneradaService = retencionGeneradaService;
        _emisionControlService = emisionControlService;
    }

    public async Task<CompraXmlPreviewDto> LeerCompraDesdeXmlAsync(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            throw new Exception("El XML está vacío.");

        var doc = XDocument.Parse(xmlContent);

        XElement? liquidacionNode = doc.Root?.Name.LocalName == "liquidacionCompra"
            ? doc.Root
            : doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "liquidacionCompra");

        if (liquidacionNode != null)
            return await LeerLiquidacionCompraDesdeXmlAsync(xmlContent, doc, liquidacionNode);

        XElement? facturaNode = doc.Root?.Name.LocalName == "factura"
            ? doc.Root
            : doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "factura");

        if (facturaNode == null)
            throw new Exception("No se encontró el nodo <liquidacionCompra> dentro del XML. Para retenciones debes cargar el XML de una liquidación de compra.");

        XElement? autorizacionNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "autorizacion");
        XElement? infoTributaria = facturaNode.Descendants().FirstOrDefault(x => x.Name.LocalName == "infoTributaria");
        XElement? infoFactura = facturaNode.Descendants().FirstOrDefault(x => x.Name.LocalName == "infoFactura");
        var detallesXml = facturaNode.Descendants().Where(x => x.Name.LocalName == "detalle").ToList();

        if (infoTributaria == null || infoFactura == null)
            throw new Exception("El XML no tiene la estructura esperada de liquidación de compra SRI.");

        if (GetValue(infoTributaria, "codDoc") != "03")
            throw new Exception("El XML seleccionado no es una liquidación de compra. Para retenciones carga un XML con codDoc 03.");

        string claveAcceso = GetValue(infoTributaria, "claveAcceso");
        string estab = GetValue(infoTributaria, "estab");
        string ptoEmi = GetValue(infoTributaria, "ptoEmi");
        string secuencial = GetValue(infoTributaria, "secuencial");
        string rucProveedor = GetValue(infoTributaria, "ruc");
        string razonSocialProveedor = GetValue(infoTributaria, "razonSocial");
        int ambiente = ParseInt(GetValue(infoTributaria, "ambiente"));

        string rucEmisor = GetValue(infoFactura, "identificacionComprador");

        string tipoIdentificacionComprador = GetValue(infoFactura, "tipoIdentificacionComprador");
        string obligadoContabilidad = GetValue(infoFactura, "obligadoContabilidad");

        string direccionMatriz = GetValue(infoTributaria, "dirMatriz");
        string direccionEstablecimiento = GetValue(infoFactura, "dirEstablecimiento");
        string direccionProveedor = direccionMatriz;

        string fechaEmisionStr = GetValue(infoFactura, "fechaEmision");
        string totalSinImpuestosStr = GetValue(infoFactura, "totalSinImpuestos");
        string totalDescuentoStr = GetValue(infoFactura, "totalDescuento");
        string importeTotalStr = GetValue(infoFactura, "importeTotal");
        string moneda = GetValue(infoFactura, "moneda");

        string formaPago = infoFactura
            .Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "formaPago")
            ?.Value?.Trim() ?? "";

        string guiaRemision = GetValue(infoFactura, "guiaRemision");
        if (string.IsNullOrWhiteSpace(guiaRemision))
        {
            guiaRemision = GetCampoAdicional(
                doc,
                "GuiaRemision",
                "guiaRemision",
                "guia de remision",
                "numeroGuiaRemision",
                "nroGuiaRemision");
        }

        guiaRemision = FormatearNumeroGuiaRemisionCompra(guiaRemision, estab, ptoEmi) ?? "";

        string telefonoEmisor = GetCampoAdicional(
            doc,
            "TelefonoEmisor",
            "telefonoEmisor",
            "TelefonoProveedor",
            "telefonoProveedor",
            "telefono",
            "teléfono",
            "telf",
            "convencional",
            "celular",
            "movil",
            "móvil",
            "telefonoMovil",
            "whatsapp");

        string telefonoFijoProveedor = telefonoEmisor;
        string telefonoMovilProveedor = telefonoEmisor;

        string emailProveedor = GetCampoAdicional(
            doc,
            "EmailEmisor",
            "emailEmisor",
            "EmailProveedor",
            "emailProveedor",
            "email",
            "correo",
            "correoElectronico",
            "correo electrónico");

        string formaPagoNombre = await ObtenerDescripcionFormaPagoAsync(formaPago);
        string tipoIdentificacionNombre = await ObtenerDescripcionTipoIdentificacionAsync(tipoIdentificacionComprador);

        DateTime? fechaEmision = ParseDate(fechaEmisionStr);

        string numeroAutorizacion =
            autorizacionNode?.Descendants().FirstOrDefault(x => x.Name.LocalName == "numeroAutorizacion")?.Value?.Trim()
            ?? claveAcceso;

        string fechaAutorizacionSri =
            autorizacionNode?.Descendants().FirstOrDefault(x => x.Name.LocalName == "fechaAutorizacion")?.Value?.Trim()
            ?? "";

        var dto = new CompraXmlPreviewDto
        {
            XmlOriginal = xmlContent,
            ClaveAcceso = claveAcceso,
            Estab = estab,
            PtoEmi = ptoEmi,
            Secuencial = secuencial,
            RucProveedor = rucProveedor,
            RazonSocialProveedor = razonSocialProveedor,
            Ambiente = ambiente,

            DireccionMatriz = direccionMatriz,
            DireccionEstablecimiento = direccionEstablecimiento,
            DireccionProveedor = direccionProveedor,

            TelefonoProveedor = telefonoMovilProveedor,
            TelefonoFijoProveedor = telefonoFijoProveedor,
            EmailProveedor = emailProveedor,

            RucEmisor = rucEmisor,
            IdentificacionComprador = rucEmisor,

            TipoIdentificacionComprador = tipoIdentificacionComprador,
            TipoIdentificacionCompradorNombre = tipoIdentificacionNombre,
            ObligadoContabilidad = obligadoContabilidad,

            FechaEmision = fechaEmision,
            FechaEmisionDocumentoSustento = fechaEmision,
            TotalSinImpuestos = ParseDecimal(totalSinImpuestosStr),
            TotalDescuento = ParseDecimal(totalDescuentoStr),
            ImporteTotal = ParseDecimal(importeTotalStr),
            Moneda = moneda,
            FormaPago = formaPago,
            FormaPagoNombre = formaPagoNombre,
            GuiaRemision = guiaRemision,
            NumeroAutorizacion = numeroAutorizacion,
            FechaAutorizacionSri = fechaAutorizacionSri,

            Subtotal0 = 0m,
            Subtotal12 = 0m,
            Subtotal5 = 0m,
            Subtotal8 = 0m,
            NoImp = 0m,
            ExIva = 0m,
            Iva = 0m,
            Iva5 = 0m,
            Iva8 = 0m,

            Detalles = new List<CompraXmlDetalleDto>(),
            Retenciones = new List<CompraRetValorDto>()
        };

        var compraExistente = await _context.ComprasFacturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CodClave == claveAcceso);

        if (compraExistente != null)
        {
            dto.YaImportado = true;
            dto.CodFacturaExistente = compraExistente.CodFactura;
            dto.GuiaRemision = FormatearNumeroGuiaRemisionCompra(
                compraExistente.GuiaRemision,
                ObtenerEstabDesdeSerie(compraExistente.Serie),
                ObtenerPtoEmiDesdeSerie(compraExistente.Serie)) ?? dto.GuiaRemision;

            var retencionesGuardadas = await _context.ComprasRetValor
                .AsNoTracking()
                .Where(x => x.IdCompra == compraExistente.CodFactura)
                .OrderBy(x => x.Sec)
                .ToListAsync();

            dto.Retenciones = retencionesGuardadas.Select(x => new CompraRetValorDto
            {
                Sec = x.Sec,
                IdRet = x.IdRet,
                Valor = x.Valor,
                Base = x.Base,
                Tipo = x.Tipo,
                Estado = x.Estado,
                Serie = x.Serie,
                NumSri = x.NumSri,
                Autorizacion = x.Autorizacion,
                IdRetencionInfo = x.IdRetencionInfo,
                PorcentajeRetencion = x.PorcentajeRetencion,
                ValorRetenido = x.ValorRetenido,
                DescripcionRet = ""
            }).ToList();
        }

        var emisor = await _context.Emisores
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Ruc == rucEmisor);

        if (emisor == null)
        {
            emisor = await _context.Emisores
                .AsNoTracking()
                .Where(e => e.Estado == true)
                .OrderByDescending(e => e.Codigo)
                .FirstOrDefaultAsync();
        }

        if (emisor != null)
        {
            dto.CodEmisor = emisor.Codigo;
            dto.NombreEmisorEncontrado = $"{emisor.NomComercial} - {emisor.Ruc}";
        }

        var proveedor = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Numeroidentificacion == rucProveedor);

        if (proveedor != null)
        {
            dto.CodProveedor = proveedor.Codcliente;

            if (string.IsNullOrWhiteSpace(dto.EmailProveedor))
                dto.EmailProveedor = proveedor.Correo ?? "";
        }

        bool clasificadoDesdeDetalle = false;

        foreach (var det in detallesXml)
        {
            decimal precioTotalSinImpuesto = ParseDecimal(GetValue(det, "precioTotalSinImpuesto"));

            var impuestosDetalle = det.Descendants()
                .Where(x => x.Name.LocalName == "impuesto")
                .ToList();

            bool tuvoIva = false;

            foreach (var imp in impuestosDetalle)
            {
                string codigo = GetValue(imp, "codigo");
                string codigoPorcentaje = GetValue(imp, "codigoPorcentaje");
                string tarifaStr = GetValue(imp, "tarifa");
                decimal baseImp = ParseDecimal(GetValue(imp, "baseImponible"));
                decimal valor = ParseDecimal(GetValue(imp, "valor"));

                if (codigo != "2")
                    continue;

                tuvoIva = true;
                clasificadoDesdeDetalle = true;

                if (baseImp <= 0m)
                    baseImp = precioTotalSinImpuesto;

                AcumularPorcentaje(dto, codigoPorcentaje, tarifaStr, baseImp, valor);
            }

            if (!tuvoIva && precioTotalSinImpuesto > 0m)
            {
                dto.Subtotal0 += precioTotalSinImpuesto;
                clasificadoDesdeDetalle = true;
            }
        }

        if (!clasificadoDesdeDetalle)
        {
            var totalConImpuestos = infoFactura
                .Descendants()
                .Where(x => x.Name.LocalName == "totalImpuesto")
                .ToList();

            foreach (var imp in totalConImpuestos)
            {
                string codigo = GetValue(imp, "codigo");
                string codigoPorcentaje = GetValue(imp, "codigoPorcentaje");
                string tarifaStr = GetValue(imp, "tarifa");
                decimal baseImp = ParseDecimal(GetValue(imp, "baseImponible"));
                decimal valor = ParseDecimal(GetValue(imp, "valor"));

                if (codigo != "2")
                    continue;

                AcumularPorcentaje(dto, codigoPorcentaje, tarifaStr, baseImp, valor);
            }
        }

        decimal subtotalClasificado =
            dto.Subtotal0 +
            dto.Subtotal5 +
            dto.Subtotal8 +
            dto.Subtotal12 +
            dto.NoImp +
            dto.ExIva;

        decimal diferencia = dto.TotalSinImpuestos - subtotalClasificado;

        if (Math.Abs(diferencia) <= 0.02m)
        {
            if (dto.Subtotal5 > 0m)
                dto.Subtotal5 += diferencia;
            else if (dto.Subtotal8 > 0m)
                dto.Subtotal8 += diferencia;
            else if (dto.Subtotal12 > 0m)
                dto.Subtotal12 += diferencia;
            else if (dto.Subtotal0 > 0m)
                dto.Subtotal0 += diferencia;
        }

        foreach (var det in detallesXml)
        {
            string codPrincipal = GetValue(det, "codigoPrincipal");
            string codAuxiliar = GetValue(det, "codigoAuxiliar");
            string descripcion = GetFirstNonEmptyValue(det, "descripcion");
            decimal cantidad = ParseDecimal(GetValue(det, "cantidad"));
            decimal precioUnitario = ParseDecimal(GetValue(det, "precioUnitario"));
            decimal descuento = ParseDecimal(GetValue(det, "descuento"));
            decimal precioTotalSinImpuesto = ParseDecimal(GetValue(det, "precioTotalSinImpuesto"));

            decimal valorIvaDetalle = 0m;
            decimal valorIceDetalle = 0m;
            int tarifa = 0;
            int codImp = 0;
            int porImp = 0;

            var impuestosDetalle = det.Descendants()
                .Where(x => x.Name.LocalName == "impuesto")
                .ToList();

            foreach (var imp in impuestosDetalle)
            {
                string codigo = GetValue(imp, "codigo");
                string codigoPorcentaje = GetValue(imp, "codigoPorcentaje");
                string tarifaStr = GetValue(imp, "tarifa");
                decimal valor = ParseDecimal(GetValue(imp, "valor"));

                codImp = ParseInt(codigo);
                porImp = ParseInt(codigoPorcentaje);
                tarifa = ParseTarifaEntera(tarifaStr);

                if (codigo == "2")
                    valorIvaDetalle += valor;
                else if (codigo == "3")
                    valorIceDetalle += valor;
            }

            dto.Detalles.Add(new CompraXmlDetalleDto
            {
                CodPrincipal = codPrincipal,
                CodAuxiliar = codAuxiliar,
                Descripcion = descripcion,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                Descuento = descuento,
                PrecioTotalSinImpuesto = precioTotalSinImpuesto,
                CodImp = codImp,
                PorImp = porImp,
                Tarifa = tarifa,
                ValorIVA = valorIvaDetalle,
                ValorICE = valorIceDetalle,
                ValorTotal = precioTotalSinImpuesto + valorIvaDetalle + valorIceDetalle
            });
        }

        var retencionesXml = doc.Descendants()
            .Where(x => x.Name.LocalName == "retencion")
            .ToList();

        foreach (var ret in retencionesXml)
        {
            string codigo = GetValue(ret, "codigo");
            string codigoRetencion = GetValue(ret, "codigoRetencion");
            string baseImponibleStr = GetValue(ret, "baseImponible");
            string porcentajeRetenerStr = GetValue(ret, "porcentajeRetener");
            string valorRetenidoStr = GetValue(ret, "valorRetenido");

            decimal baseImponible = ParseDecimal(baseImponibleStr);
            decimal porcentajeRetener = ParseDecimal(porcentajeRetenerStr);
            decimal valorRetenido = ParseDecimal(valorRetenidoStr);

            string tipo = codigo switch
            {
                "1" => "RENTA",
                "2" => "IVA",
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(tipo))
                continue;

            int? idRet = null;
            string descripcion = "";

            if (tipo == "IVA")
            {
                if (int.TryParse(codigoRetencion, out var ivaId))
                {
                    idRet = ivaId;

                    var ivaDb = await _context.RetencionIva
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Codigo == ivaId);

                    if (ivaDb != null)
                        descripcion = ivaDb.Descripcion ?? "";
                }
            }
            else if (tipo == "RENTA")
            {
                var rentaDb = await _context.RetencionRenta
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Codigo == codigoRetencion);

                if (rentaDb != null)
                {
                    descripcion = rentaDb.Descripcion ?? "";

                    if (int.TryParse(rentaDb.Codigo, out var rentaId))
                        idRet = rentaId;
                }
                else if (int.TryParse(codigoRetencion, out var rentaId))
                {
                    idRet = rentaId;
                }
            }

            dto.Retenciones.Add(new CompraRetValorDto
            {
                Sec = null,
                IdRet = idRet,
                Tipo = tipo,
                Base = baseImponible,
                Valor = porcentajeRetener,
                PorcentajeRetencion = porcentajeRetener,
                ValorRetenido = valorRetenido,
                DescripcionRet = descripcion,
                Estado = true,
                Serie = dto.Serie,
                Autorizacion = dto.NumeroAutorizacion
            });
        }

        return dto;
    }

    private async Task<CompraXmlPreviewDto> LeerLiquidacionCompraDesdeXmlAsync(
        string xmlContent,
        XDocument doc,
        XElement liquidacionNode)
    {
        XElement? autorizacionNode = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "autorizacion");
        XElement? infoTributaria = liquidacionNode.Descendants().FirstOrDefault(x => x.Name.LocalName == "infoTributaria");
        XElement? infoLiquidacion = liquidacionNode.Descendants().FirstOrDefault(x => x.Name.LocalName == "infoLiquidacionCompra");
        var detallesXml = liquidacionNode.Descendants().Where(x => x.Name.LocalName == "detalle").ToList();

        if (infoTributaria == null || infoLiquidacion == null)
            throw new Exception("El XML no tiene la estructura esperada de liquidación de compra SRI.");

        string claveAcceso = GetValue(infoTributaria, "claveAcceso");
        string estab = GetValue(infoTributaria, "estab");
        string ptoEmi = GetValue(infoTributaria, "ptoEmi");
        string secuencial = GetValue(infoTributaria, "secuencial");
        string rucEmisor = GetValue(infoTributaria, "ruc");
        int ambiente = ParseInt(GetValue(infoTributaria, "ambiente"));

        string rucProveedor = GetValue(infoLiquidacion, "identificacionProveedor");
        string razonSocialProveedor = GetValue(infoLiquidacion, "razonSocialProveedor");
        string tipoIdentificacionProveedor = GetValue(infoLiquidacion, "tipoIdentificacionProveedor");
        string obligadoContabilidad = GetValue(infoLiquidacion, "obligadoContabilidad");
        string direccionMatriz = GetValue(infoTributaria, "dirMatriz");
        string direccionEstablecimiento = GetValue(infoLiquidacion, "dirEstablecimiento");
        string direccionProveedor = GetValue(infoLiquidacion, "direccionProveedor");
        string fechaEmisionStr = GetValue(infoLiquidacion, "fechaEmision");
        string totalSinImpuestosStr = GetValue(infoLiquidacion, "totalSinImpuestos");
        string totalDescuentoStr = GetValue(infoLiquidacion, "totalDescuento");
        string importeTotalStr = GetValue(infoLiquidacion, "importeTotal");
        string moneda = GetValue(infoLiquidacion, "moneda");

        string formaPago = infoLiquidacion
            .Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "formaPago")
            ?.Value?.Trim() ?? "";

        string telefonoProveedor = GetCampoAdicional(
            doc,
            "TelefonoProveedor",
            "telefonoProveedor",
            "telefono",
            "teléfono",
            "celular",
            "movil",
            "móvil",
            "whatsapp");

        string emailProveedor = GetCampoAdicional(
            doc,
            "EmailProveedor",
            "emailProveedor",
            "email",
            "correo",
            "correoElectronico",
            "correo electrónico");

        string numeroAutorizacion =
            autorizacionNode?.Descendants().FirstOrDefault(x => x.Name.LocalName == "numeroAutorizacion")?.Value?.Trim()
            ?? claveAcceso;

        string fechaAutorizacionSri =
            autorizacionNode?.Descendants().FirstOrDefault(x => x.Name.LocalName == "fechaAutorizacion")?.Value?.Trim()
            ?? "";

        var dto = new CompraXmlPreviewDto
        {
            XmlOriginal = xmlContent,
            ClaveAcceso = claveAcceso,
            Estab = estab,
            PtoEmi = ptoEmi,
            Secuencial = secuencial,
            RucProveedor = rucProveedor,
            RazonSocialProveedor = razonSocialProveedor,
            Ambiente = ambiente,
            DireccionMatriz = direccionMatriz,
            DireccionEstablecimiento = direccionEstablecimiento,
            DireccionProveedor = direccionProveedor,
            TelefonoProveedor = telefonoProveedor,
            TelefonoFijoProveedor = telefonoProveedor,
            EmailProveedor = emailProveedor,
            RucEmisor = rucEmisor,
            IdentificacionComprador = rucEmisor,
            TipoIdentificacionComprador = tipoIdentificacionProveedor,
            TipoIdentificacionCompradorNombre = await ObtenerDescripcionTipoIdentificacionAsync(tipoIdentificacionProveedor),
            ObligadoContabilidad = obligadoContabilidad,
            FechaEmision = ParseDate(fechaEmisionStr),
            FechaEmisionDocumentoSustento = ParseDate(fechaEmisionStr),
            TotalSinImpuestos = ParseDecimal(totalSinImpuestosStr),
            TotalDescuento = ParseDecimal(totalDescuentoStr),
            ImporteTotal = ParseDecimal(importeTotalStr),
            Moneda = string.IsNullOrWhiteSpace(moneda) ? "DOLAR" : moneda,
            FormaPago = formaPago,
            FormaPagoNombre = await ObtenerDescripcionFormaPagoAsync(formaPago),
            NumeroAutorizacion = numeroAutorizacion,
            FechaAutorizacionSri = fechaAutorizacionSri,
            NumeroRetencionGenerado = await GenerarSecuenciaRetencionAsync(),
            Detalles = new List<CompraXmlDetalleDto>(),
            Retenciones = new List<CompraRetValorDto>()
        };

        var liquidacionExistente = await _context.ComprasFacturas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CodDocumento == "03" && x.CodClave == claveAcceso);

        if (liquidacionExistente != null)
        {
            dto.YaImportado = true;
            dto.CodFacturaExistente = liquidacionExistente.CodFactura;
            dto.NumeroRetencionGenerado = string.IsNullOrWhiteSpace(liquidacionExistente.NumRetencion)
                ? dto.NumeroRetencionGenerado
                : liquidacionExistente.NumRetencion!;
        }

        var emisor = await _context.Emisores
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Ruc == rucEmisor);

        if (emisor != null)
        {
            dto.CodEmisor = emisor.Codigo;
            dto.NombreEmisorEncontrado = $"{emisor.NomComercial} - {emisor.Ruc}";
        }

        var proveedor = await _context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Numeroidentificacion == rucProveedor);

        if (proveedor != null)
        {
            dto.CodProveedor = proveedor.Codcliente;
            if (string.IsNullOrWhiteSpace(dto.DireccionProveedor))
                dto.DireccionProveedor = proveedor.Direccion ?? "";
            if (string.IsNullOrWhiteSpace(dto.TelefonoProveedor))
                dto.TelefonoProveedor = proveedor.Celular ?? proveedor.Telefonoconvencional ?? "";
            if (string.IsNullOrWhiteSpace(dto.EmailProveedor))
                dto.EmailProveedor = proveedor.Correo ?? "";
        }

        foreach (var imp in infoLiquidacion.Descendants().Where(x => x.Name.LocalName == "totalImpuesto"))
        {
            if (GetValue(imp, "codigo") != "2")
                continue;

            AcumularPorcentaje(
                dto,
                GetValue(imp, "codigoPorcentaje"),
                GetValue(imp, "tarifa"),
                ParseDecimal(GetValue(imp, "baseImponible")),
                ParseDecimal(GetValue(imp, "valor")));
        }

        foreach (var det in detallesXml)
        {
            string codPrincipal = GetValue(det, "codigoPrincipal");
            string codAuxiliar = GetValue(det, "codigoAuxiliar");
            string descripcion = GetFirstNonEmptyValue(det, "descripcion");
            decimal cantidad = ParseDecimal(GetValue(det, "cantidad"));
            decimal precioUnitario = ParseDecimal(GetValue(det, "precioUnitario"));
            decimal descuento = ParseDecimal(GetValue(det, "descuento"));
            decimal precioTotalSinImpuesto = ParseDecimal(GetValue(det, "precioTotalSinImpuesto"));
            decimal valorIvaDetalle = 0m;
            int tarifa = 0;
            int codImp = 0;
            int porImp = 0;

            foreach (var imp in det.Descendants().Where(x => x.Name.LocalName == "impuesto"))
            {
                string codigo = GetValue(imp, "codigo");
                codImp = ParseInt(codigo);
                porImp = ParseInt(GetValue(imp, "codigoPorcentaje"));
                tarifa = ParseTarifaEntera(GetValue(imp, "tarifa"));

                if (codigo == "2")
                    valorIvaDetalle += ParseDecimal(GetValue(imp, "valor"));
            }

            dto.Detalles.Add(new CompraXmlDetalleDto
            {
                CodPrincipal = codPrincipal,
                CodAuxiliar = codAuxiliar,
                Descripcion = descripcion,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                Descuento = descuento,
                PrecioTotalSinImpuesto = precioTotalSinImpuesto,
                CodImp = codImp,
                PorImp = porImp,
                Tarifa = tarifa,
                ValorIVA = valorIvaDetalle,
                ValorICE = 0m,
                ValorTotal = precioTotalSinImpuesto + valorIvaDetalle
            });
        }

        return dto;
    }

    public async Task<CompraXmlPreviewDto> CrearPreviewManualAsync()
    {
        var dto = new CompraXmlPreviewDto
        {
            EsManual = true,
            XmlOriginal = "",
            ClaveAcceso = "",
            Estab = "001",
            PtoEmi = "001",
            Secuencial = "",
            RucProveedor = "",
            RazonSocialProveedor = "",
            Ambiente = 2,

            DireccionMatriz = "",
            DireccionEstablecimiento = "",
            DireccionProveedor = "",
            TelefonoProveedor = "",
            TelefonoFijoProveedor = "",
            EmailProveedor = "",

            RucEmisor = "",
            IdentificacionComprador = "",
            TipoIdentificacionComprador = "04",
            TipoIdentificacionCompradorNombre = await ObtenerDescripcionTipoIdentificacionAsync("04"),
            ObligadoContabilidad = "NO",

            FechaEmision = DateTime.Today,
            FechaEmisionDocumentoSustento = DateTime.Today,
            TotalSinImpuestos = 0m,
            TotalDescuento = 0m,
            ImporteTotal = 0m,
            Moneda = "DOLAR",
            FormaPago = "20",
            FormaPagoNombre = await ObtenerDescripcionFormaPagoAsync("20"),

            NumeroAutorizacion = "",
            FechaAutorizacionSri = DateTime.Today.ToString("dd/MM/yyyy"),

            Subtotal0 = 0m,
            Subtotal12 = 0m,
            Subtotal5 = 0m,
            Subtotal8 = 0m,
            NoImp = 0m,
            ExIva = 0m,
            Iva = 0m,
            Iva5 = 0m,
            Iva8 = 0m,

            Detalles = new List<CompraXmlDetalleDto>(),
            Retenciones = new List<CompraRetValorDto>()
        };

        return dto;
    }

    public async Task ResolverDatosManualAsync(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            return;

        preview.TipoIdentificacionCompradorNombre =
            await ObtenerDescripcionTipoIdentificacionAsync(preview.TipoIdentificacionComprador ?? "");

        preview.FormaPagoNombre =
            await ObtenerDescripcionFormaPagoAsync(preview.FormaPago ?? "");

        preview.GuiaRemision = FormatearNumeroGuiaRemisionCompra(
            preview.GuiaRemision,
            preview.Estab,
            preview.PtoEmi) ?? "";

        preview.CodEmisor = null;
        preview.NombreEmisorEncontrado = "";

        preview.IdentificacionComprador = preview.RucEmisor ?? "";

        await AplicarEmisorConfiguradoAsync(preview);

        preview.CodProveedor = null;

        if (!string.IsNullOrWhiteSpace(preview.RucProveedor))
        {
            var proveedorCliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Numeroidentificacion == preview.RucProveedor);

            if (proveedorCliente != null)
            {
                preview.CodProveedor = proveedorCliente.Codcliente;

                if (string.IsNullOrWhiteSpace(preview.EmailProveedor))
                    preview.EmailProveedor = proveedorCliente.Correo ?? "";
            }
        }

        RecalcularTotalesManual(preview);
    }

    public void RecalcularTotalesManual(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            return;

        preview.Subtotal0 = Red2(preview.Subtotal0);
        preview.Subtotal5 = Red2(preview.Subtotal5);
        preview.Subtotal8 = Red2(preview.Subtotal8);
        preview.Subtotal12 = Red2(preview.Subtotal12);
        preview.NoImp = Red2(preview.NoImp);
        preview.ExIva = Red2(preview.ExIva);
        preview.TotalDescuento = Red2(preview.TotalDescuento);

        preview.Iva5 = Red2(preview.Iva5);
        preview.Iva8 = Red2(preview.Iva8);

        decimal subtotalBase = Red2(
            preview.Subtotal0 +
            preview.Subtotal5 +
            preview.Subtotal8 +
            preview.Subtotal12 +
            preview.NoImp +
            preview.ExIva
        );

        preview.TotalSinImpuestos = subtotalBase;
        preview.Iva = Red2(preview.Iva5 + preview.Iva8 + CalcularIva15DesdeSubtotal12(preview.Subtotal12));
        preview.ImporteTotal = Red2(preview.TotalSinImpuestos - preview.TotalDescuento + preview.Iva);
    }

    private decimal CalcularIva15DesdeSubtotal12(decimal subtotal15Visual)
    {
        return Red2(subtotal15Visual * 0.15m);
    }

    private decimal Red2(decimal valor)
    {
        return Math.Round(valor, 2, MidpointRounding.AwayFromZero);
    }

    private string GenerarClaveManualTemporal(CompraXmlPreviewDto preview)
    {
        string fecha = (preview.FechaEmision ?? DateTime.Today).ToString("ddMMyyyy");
        string serie = $"{(preview.Estab ?? "001").PadLeft(3, '0')}{(preview.PtoEmi ?? "001").PadLeft(3, '0')}";
        string sec = (preview.Secuencial ?? "1").PadLeft(9, '0');

        string ruc = string.IsNullOrWhiteSpace(preview.RucEmisor)
            ? "9999999999999"
            : preview.RucEmisor.PadLeft(13, '0');

        preview.IdentificacionComprador = preview.RucEmisor ?? "";

        string baseClave = $"{fecha}01{ruc}{preview.Ambiente}{serie}{sec}123456781";
        return new string(baseClave.Take(49).ToArray()).PadRight(49, '0');
    }

    public async Task<int> GuardarCompraDesdePreviewAsync(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay datos para guardar.");

        preview.Detalles ??= new List<CompraXmlDetalleDto>();
        preview.Retenciones ??= new List<CompraRetValorDto>();

        if (preview.Retenciones.Any())
        {
            await _emisionControlService.AsegurarPuedeEmitirAsync(preview.Usuario);
        }

        preview.IdentificacionComprador = preview.RucEmisor ?? preview.IdentificacionComprador ?? "";
        preview.GuiaRemision = FormatearNumeroGuiaRemisionCompra(
            preview.GuiaRemision,
            preview.Estab,
            preview.PtoEmi) ?? "";

        if (preview.EsManual)
        {
            if (string.IsNullOrWhiteSpace(preview.Estab))
                preview.Estab = "001";

            if (string.IsNullOrWhiteSpace(preview.PtoEmi))
                preview.PtoEmi = "001";

            if (string.IsNullOrWhiteSpace(preview.Secuencial))
                throw new Exception("En ingreso manual debes escribir el secuencial.");

            if (string.IsNullOrWhiteSpace(preview.ClaveAcceso))
                preview.ClaveAcceso = GenerarClaveManualTemporal(preview);

            if (string.IsNullOrWhiteSpace(preview.NumeroAutorizacion))
                preview.NumeroAutorizacion = preview.ClaveAcceso;

            if (string.IsNullOrWhiteSpace(preview.FechaAutorizacionSri))
                preview.FechaAutorizacionSri = (preview.FechaEmision ?? DateTime.Today).ToString("dd/MM/yyyy");

            ValidarCompraManual(preview);
        }

        preview.Detalles ??= new List<CompraXmlDetalleDto>();
        preview.Retenciones ??= new List<CompraRetValorDto>();

        ValidarRetencionesContraTotales(preview);

        if (!preview.CodEmisor.HasValue || preview.CodEmisor.Value <= 0)
            throw new Exception("No se encontró un emisor válido para guardar la compra.");

        var emisor = await _context.Emisores
            .FirstOrDefaultAsync(e => e.Codigo == preview.CodEmisor.Value);

        if (emisor == null)
            throw new Exception("No se encontró el emisor relacionado con la compra.");

        ComprasFactura? compraExistente = null;

        if (preview.YaImportado && preview.CodFacturaExistente.HasValue)
        {
            compraExistente = await _context.ComprasFacturas
                .FirstOrDefaultAsync(x =>
                    x.CodFactura == preview.CodFacturaExistente.Value &&
                    x.CodDocumento == "03" &&
                    x.Estado == true);

            if (compraExistente == null)
                throw new Exception("No se encontró la liquidación de compra existente.");
        }

        var compraDuplicada = compraExistente == null
            ? await _context.ComprasFacturas
                .FirstOrDefaultAsync(x =>
                    x.CodClave == preview.ClaveAcceso &&
                    x.CodDocumento == "03" &&
                    x.Usuario == preview.Usuario)
            : null;

        if (compraDuplicada != null)
            compraExistente = compraDuplicada;

        if (compraExistente != null)
        {
            var retencionExistente = await _context.RetencionInfo
                .FirstOrDefaultAsync(x => x.IcCompra == compraExistente.CodFactura);

            if (retencionExistente != null)
            {
                var usuarioCompra = compraExistente.Usuario ?? preview.Usuario;
                if (usuarioCompra > 0 && retencionExistente.Usuario != usuarioCompra)
                {
                    retencionExistente.Usuario = usuarioCompra;
                    await _context.SaveChangesAsync();
                }

                throw new Exception($"La liquidación ya tiene la retención {retencionExistente.NumRetencion ?? retencionExistente.Sec.ToString()} registrada.");
            }
        }

        ComprasFactura? compra = null;
        RetencionInfo? retencionInfo = null;
        var executionStrategy = _context.Database.CreateExecutionStrategy();

        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var proveedor = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Numeroidentificacion == preview.RucProveedor);

                if (proveedor == null)
                {
                    proveedor = new Cliente
                    {
                        Numeroidentificacion = string.IsNullOrWhiteSpace(preview.RucProveedor)
                            ? "9999999999999"
                            : preview.RucProveedor,

                        Tipoidentificacion = string.IsNullOrWhiteSpace(preview.TipoIdentificacionComprador)
                            ? null
                            : preview.TipoIdentificacionComprador,

                        Nombrerazonsocial = string.IsNullOrWhiteSpace(preview.RazonSocialProveedor)
                            ? "PROVEEDOR XML"
                            : preview.RazonSocialProveedor,

                        Nombrecomercial = string.IsNullOrWhiteSpace(preview.RazonSocialProveedor)
                            ? "PROVEEDOR XML"
                            : preview.RazonSocialProveedor,

                        Nombres = string.IsNullOrWhiteSpace(preview.RazonSocialProveedor)
                            ? "PROVEEDOR"
                            : preview.RazonSocialProveedor,

                        Apellidos = ".",
                        Direccion = NormalizarTextoVacio(preview.DireccionProveedor),
                        Telefonoconvencional = LimpiarTelefono(preview.TelefonoFijoProveedor),
                        Celular = LimpiarTelefono(preview.TelefonoProveedor),
                        Correo = NormalizarTextoVacio(preview.EmailProveedor),

                        Oblgconta = string.IsNullOrWhiteSpace(preview.ObligadoContabilidad)
                            ? null
                            : preview.ObligadoContabilidad,

                        TipoCliente = 2,
                        Estado = true
                    };

                    _context.Clientes.Add(proveedor);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    bool cambioCliente = false;

                if (!string.IsNullOrWhiteSpace(preview.TipoIdentificacionComprador) &&
                    proveedor.Tipoidentificacion != preview.TipoIdentificacionComprador)
                {
                    proveedor.Tipoidentificacion = preview.TipoIdentificacionComprador;
                    cambioCliente = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.ObligadoContabilidad) &&
                    proveedor.Oblgconta != preview.ObligadoContabilidad)
                {
                    proveedor.Oblgconta = preview.ObligadoContabilidad;
                    cambioCliente = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
                {
                    if (proveedor.Nombrerazonsocial != preview.RazonSocialProveedor)
                    {
                        proveedor.Nombrerazonsocial = preview.RazonSocialProveedor;
                        cambioCliente = true;
                    }

                    if (proveedor.Nombrecomercial != preview.RazonSocialProveedor)
                    {
                        proveedor.Nombrecomercial = preview.RazonSocialProveedor;
                        cambioCliente = true;
                    }

                    if (proveedor.Nombres != preview.RazonSocialProveedor)
                    {
                        proveedor.Nombres = preview.RazonSocialProveedor;
                        cambioCliente = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(preview.DireccionProveedor) &&
                    proveedor.Direccion != preview.DireccionProveedor.Trim())
                {
                    proveedor.Direccion = preview.DireccionProveedor.Trim();
                    cambioCliente = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor) &&
                    proveedor.Telefonoconvencional != preview.TelefonoFijoProveedor.Trim())
                {
                    proveedor.Telefonoconvencional = preview.TelefonoFijoProveedor.Trim();
                    cambioCliente = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor) &&
                    proveedor.Celular != preview.TelefonoProveedor.Trim())
                {
                    proveedor.Celular = preview.TelefonoProveedor.Trim();
                    cambioCliente = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.EmailProveedor) &&
                    proveedor.Correo != preview.EmailProveedor.Trim())
                {
                    proveedor.Correo = preview.EmailProveedor.Trim();
                    cambioCliente = true;
                }

                if (cambioCliente)
                    await _context.SaveChangesAsync();
            }

            var proveedorDb = await _context.Proveedores
                .FirstOrDefaultAsync(p => p.ruc == preview.RucProveedor);

            if (proveedorDb == null && !string.IsNullOrWhiteSpace(preview.RucProveedor))
            {
                var nuevoProveedor = new Proveedor
                {
                    ruc = preview.RucProveedor,
                    nombre = preview.RazonSocialProveedor,
                    nombreComercial = preview.RazonSocialProveedor,
                    direccion = NormalizarTextoVacio(preview.DireccionProveedor),
                    telefono = LimpiarTelefono(preview.TelefonoFijoProveedor),
                    telefonoMovil = LimpiarTelefono(preview.TelefonoProveedor),
                    email = NormalizarTextoVacio(preview.EmailProveedor),
                    estado = true,
                    fechaActualizacion = DateTime.Now,
                    tipoIdentificacion = string.IsNullOrWhiteSpace(preview.TipoIdentificacionComprador)
                        ? null
                        : preview.TipoIdentificacionComprador,
                    personaNatural = '0',
                    obligado = ConvertirObligado(preview.ObligadoContabilidad),
                    formaPago = "20",
                    plazoPago = 0,
                    saldoInicial = 0
                };

                _context.Proveedores.Add(nuevoProveedor);
                await _context.SaveChangesAsync();
            }
            else if (proveedorDb != null)
            {
                bool cambioProveedor = false;

                if (!string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
                {
                    if (proveedorDb.nombre != preview.RazonSocialProveedor)
                    {
                        proveedorDb.nombre = preview.RazonSocialProveedor;
                        cambioProveedor = true;
                    }

                    if (proveedorDb.nombreComercial != preview.RazonSocialProveedor)
                    {
                        proveedorDb.nombreComercial = preview.RazonSocialProveedor;
                        cambioProveedor = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(preview.DireccionProveedor) &&
                    proveedorDb.direccion != preview.DireccionProveedor.Trim())
                {
                    proveedorDb.direccion = preview.DireccionProveedor.Trim();
                    cambioProveedor = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.TelefonoFijoProveedor) &&
                    proveedorDb.telefono != preview.TelefonoFijoProveedor.Trim())
                {
                    proveedorDb.telefono = preview.TelefonoFijoProveedor.Trim();
                    cambioProveedor = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor) &&
                    proveedorDb.telefonoMovil != preview.TelefonoProveedor.Trim())
                {
                    proveedorDb.telefonoMovil = preview.TelefonoProveedor.Trim();
                    cambioProveedor = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.EmailProveedor) &&
                    proveedorDb.email != preview.EmailProveedor.Trim())
                {
                    proveedorDb.email = preview.EmailProveedor.Trim();
                    cambioProveedor = true;
                }

                if (!string.IsNullOrWhiteSpace(preview.TipoIdentificacionComprador) &&
                    proveedorDb.tipoIdentificacion != preview.TipoIdentificacionComprador)
                {
                    proveedorDb.tipoIdentificacion = preview.TipoIdentificacionComprador;
                    cambioProveedor = true;
                }

                var obligadoXml = ConvertirObligado(preview.ObligadoContabilidad);
                if (obligadoXml.HasValue && proveedorDb.obligado != obligadoXml)
                {
                    proveedorDb.obligado = obligadoXml;
                    cambioProveedor = true;
                }

                if (proveedorDb.formaPago != "20")
                {
                    proveedorDb.formaPago = "20";
                    cambioProveedor = true;
                }

                if (cambioProveedor)
                {
                    proveedorDb.fechaActualizacion = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }

            var retencionesValidas = preview.Retenciones
                .Where(x =>
                    x.IdRet.HasValue ||
                    (x.Valor ?? 0) > 0 ||
                    !string.IsNullOrWhiteSpace(x.Tipo))
                .ToList();

            string numeroRetencionGenerado = "";

            if (retencionesValidas.Any())
            {
                numeroRetencionGenerado = string.IsNullOrWhiteSpace(preview.NumeroRetencionGenerado)
                    ? await GenerarSecuenciaRetencionAsync(preview.Usuario, preview.Serie)
                    : new string(preview.NumeroRetencionGenerado.Where(char.IsDigit).ToArray()).PadLeft(9, '0')[^9..];
                preview.NumeroRetencionGenerado = numeroRetencionGenerado;
            }

            DateTime? fechaAutorizacion = ParseDate(preview.FechaAutorizacionSri) ?? preview.FechaEmision;

            if (compraExistente != null)
            {
                compra = compraExistente;
                compra.NumRetencion = string.IsNullOrWhiteSpace(numeroRetencionGenerado)
                    ? compra.NumRetencion
                    : numeroRetencionGenerado;
                compra.TieneRetencion = retencionesValidas.Any() || compra.TieneRetencion == true;
                compra.FechaRegistro = compra.FechaRegistro ?? DateTime.Now;
                await _context.SaveChangesAsync();
            }
            else
            {
                var idVendedor = await _context.Usuarios
                    .AsNoTracking()
                    .Where(x => x.IdUsuario == preview.Usuario)
                    .Select(x => x.IdVendedor)
                    .FirstOrDefaultAsync();

                compra = new ComprasFactura
            {
                CodClave = preview.ClaveAcceso,
                CodClientes = proveedor.Codcliente,
                CodEmisor = emisor.Codigo,
                CodDocumento = "03",
                FchAutorizacion = preview.FechaEmision ?? fechaAutorizacion,
                NumFactura = preview.Secuencial,
                NumAutorizacion = preview.NumeroAutorizacion,
                GuiaRemision = string.IsNullOrWhiteSpace(preview.GuiaRemision)
                    ? null
                    : preview.GuiaRemision.Trim(),
                NumRetencion = string.IsNullOrWhiteSpace(numeroRetencionGenerado)
                    ? null
                    : numeroRetencionGenerado,

                Subtotal12 = preview.Subtotal12,
                Subtotal0 = preview.Subtotal0,
                Subtotal = preview.TotalSinImpuestos,
                Descuentos = preview.TotalDescuento,
                Iva = preview.Iva,
                ValorTotal = preview.ImporteTotal,
                NoImp = preview.NoImp,
                ExIva = preview.ExIva,
                ValorICE = preview.Detalles.Sum(x => x.ValorICE),

                Usuario = preview.Usuario,
                IdVendedor = idVendedor,
                Autorizado = "1",
                Mensaje = "Liquidacion de compra importada desde XML",
                IdEmpresa = emisor.IdEmpresa,
                IdSucursal = emisor.IdSucursal,
                Serie = preview.Serie,
                FechaAutoSRI = preview.FechaAutorizacionSri,
                TipoPago = string.IsNullOrWhiteSpace(preview.FormaPago) ? "20" : preview.FormaPago,
                EstadoEnvioSRI = "RECIBIDO",
                SubCeroTotal = preview.Subtotal0,
                SubDoceTotal = preview.Subtotal12,
                SubNoImpTotal = preview.NoImp,
                SubExIvaTotal = preview.ExIva,
                Ambiente = preview.Ambiente,
                Estado = true,
                TipoDocumento = "LIQ",
                FechaRegistro = DateTime.Now,
                Inventario = false,
                Contabilizado = false,

                Subtotal5 = preview.Subtotal5,
                Subtotal8 = preview.Subtotal8,
                Iva5 = preview.Iva5,
                Iva8 = preview.Iva8,

                TieneRetencion = retencionesValidas.Any()
            };

                _context.ComprasFacturas.Add(compra);
                await _context.SaveChangesAsync();

                foreach (var item in preview.Detalles)
                {
                    var detalle = new ComprasDetalleFac
                    {
                        CodFactura = compra.CodFactura,
                        CodProducto = 0,
                        CodPrincipal = item.CodPrincipal,
                        CodAuxiliar = item.CodAuxiliar,
                        CantProducto = item.Cantidad,
                        DescripProducto = item.Descripcion,
                        PrecioProducto = item.PrecioUnitario,
                        Descuento = item.Descuento,
                        ValorTProducto = item.PrecioTotalSinImpuesto,
                        ValorICE = item.ValorICE,
                        ValorIVA = item.ValorIVA,
                        ValorTotal = item.ValorTotal,
                        CodImp = item.CodImp,
                        PorImp = item.PorImp,
                        Tarifa = item.Tarifa,
                        Inventariado = false,
                        Observacion = $"Liquidacion XML - Moneda: {preview.Moneda}"
                    };

                    _context.ComprasDetalleFac.Add(detalle);
                }
            }

            if (retencionesValidas.Any())
            {
                var fechaRetencion = DateTime.Today;

                retencionInfo = new RetencionInfo
                {
                    NumRetencion = numeroRetencionGenerado,
                    Fecha = fechaRetencion,
                    PeriodoFiscal = fechaRetencion.ToString("MM/yyyy"),
                    TipoDocumento = "03",
                    TipoIdentificacion = preview.TipoIdentificacionComprador,
                    IdCliente = preview.RucProveedor,
                    Clave = string.IsNullOrWhiteSpace(preview.ClaveAccesoRetencion)
                        ? preview.ClaveAcceso
                        : preview.ClaveAccesoRetencion,
                    NombreXml = string.IsNullOrWhiteSpace(preview.NombreArchivoRetencion)
                        ? null
                        : preview.NombreArchivoRetencion,
                    NumAutorizacion = null,
                    FechaAutorizaSri = null,
                    Usuario = preview.Usuario,
                    Autorizado = null,
                    Mensaje = "Retención generada pendiente de autorización SRI",
                    IdEmpresa = emisor.IdEmpresa,
                    IdSucursal = emisor.IdSucursal,
                    Serie = preview.Serie,
                    Ambiente = preview.Ambiente,
                    IcCompra = compra.CodFactura,
                    Estado = "ACTIVO"
                };

                var ivaList = retencionesValidas
                    .Where(x => (x.Tipo ?? "").Trim().ToUpper() == "IVA")
                    .ToList();

                var rentaList = retencionesValidas
                    .Where(x => (x.Tipo ?? "").Trim().ToUpper() == "RENTA")
                    .ToList();

                if (ivaList.Count > 0)
                {
                    retencionInfo.IdRetIva = string.IsNullOrWhiteSpace(ivaList[0].CodigoRetencion) ? ivaList[0].IdRet?.ToString() : ivaList[0].CodigoRetencion;
                    retencionInfo.DescripcionRetIva = ivaList[0].DescripcionRet;
                    retencionInfo.BaseRetIva = ivaList[0].Base ?? 0m;
                    retencionInfo.ValorRetIva = ivaList[0].ValorRetenido ?? 0m;
                    retencionInfo.TipoRetIva = ivaList[0].Tipo;
                }

                if (ivaList.Count > 1)
                {
                    retencionInfo.IdRetIva1 = string.IsNullOrWhiteSpace(ivaList[1].CodigoRetencion) ? ivaList[1].IdRet?.ToString() : ivaList[1].CodigoRetencion;
                    retencionInfo.DescripcionRetIva1 = ivaList[1].DescripcionRet;
                    retencionInfo.BaseRetIva1 = ivaList[1].Base ?? 0m;
                    retencionInfo.ValorRetIva1 = ivaList[1].ValorRetenido ?? 0m;
                    retencionInfo.TipoRetIva1 = ivaList[1].Tipo;
                }

                if (rentaList.Count > 0)
                {
                    retencionInfo.IdRetRenta = string.IsNullOrWhiteSpace(rentaList[0].CodigoRetencion) ? rentaList[0].IdRet?.ToString() : rentaList[0].CodigoRetencion;
                    retencionInfo.DescripcionRetRenta = rentaList[0].DescripcionRet;
                    retencionInfo.BaseRetRenta = rentaList[0].Base ?? 0m;
                    retencionInfo.ValorRetRenta = rentaList[0].ValorRetenido ?? 0m;
                    retencionInfo.TipoRetRenta = rentaList[0].Tipo;
                }

                if (rentaList.Count > 1)
                {
                    retencionInfo.IdRetRenta1 = string.IsNullOrWhiteSpace(rentaList[1].CodigoRetencion) ? rentaList[1].IdRet?.ToString() : rentaList[1].CodigoRetencion;
                    retencionInfo.DescripcionRetRenta1 = rentaList[1].DescripcionRet;
                    retencionInfo.BaseRetRenta1 = rentaList[1].Base ?? 0m;
                    retencionInfo.ValorRetRenta1 = rentaList[1].ValorRetenido ?? 0m;
                    retencionInfo.TipoRetRenta1 = rentaList[1].Tipo;
                }

                _context.RetencionInfo.Add(retencionInfo);
                await _context.SaveChangesAsync();
            }

            foreach (var ret in retencionesValidas)
            {
                if (string.IsNullOrWhiteSpace(ret.Tipo))
                    throw new Exception("Debes seleccionar el tipo de retención.");

                if (!ret.IdRet.HasValue || ret.IdRet.Value <= 0)
                    throw new Exception("Debes ingresar un código válido en IdRet.");

                var tipo = ret.Tipo.Trim().ToUpperInvariant();

                if (tipo != "IVA" && tipo != "RENTA")
                    throw new Exception("El tipo de retención debe ser IVA o RENTA.");

                decimal valorRetenido = ret.ValorRetenido ?? 0m;
                decimal baseRet = ret.Base ?? 0m;

                if (valorRetenido < 0m)
                    throw new Exception("El valor retenido no puede ser negativo.");

                if (baseRet < 0m)
                    throw new Exception("La base de la retención no puede ser negativa.");

                if (valorRetenido > baseRet)
                {
                    if (tipo == "RENTA")
                        throw new Exception("La retención de renta no puede ser mayor a la Base Imponible de renta.");

                    if (tipo == "IVA")
                        throw new Exception("La retención de IVA no puede ser mayor a la Base Imponible de IVA.");

                    throw new Exception("El valor retenido no puede ser mayor a la base imponible.");
                }

                if (tipo == "RENTA")
                {
                    if (valorRetenido > preview.TotalSinImpuestos)
                        throw new Exception($"La retención de renta no puede ser mayor al Total sin Impuestos ({preview.TotalSinImpuestos:N2}).");

                    if (baseRet > preview.TotalSinImpuestos)
                        throw new Exception($"La base de la retención de renta no puede ser mayor al Total sin Impuestos ({preview.TotalSinImpuestos:N2}).");
                }

                if (tipo == "IVA")
                {
                    if (valorRetenido > preview.Iva)
                        throw new Exception($"La retención IVA no puede ser mayor al IVA Total ({preview.Iva:N2}).");

                    if (baseRet > preview.Iva)
                        throw new Exception($"La base de la retención IVA no puede ser mayor al IVA Total ({preview.Iva:N2}).");
                }

                await ValidarRetencionAsync(tipo, ret.IdRet, ret.CodigoRetencion);

                var item = new CompraRetValor
                {
                    IdCompra = compra.CodFactura,
                    IdRet = ret.IdRet,
                    Valor = ret.Valor ?? 0m,
                    Base = ret.Base ?? 0m,
                    Tipo = tipo,
                    Estado = true,
                    Serie = string.IsNullOrWhiteSpace(ret.Serie) ? preview.Serie : ret.Serie,
                    NumSri = ret.NumSri,
                    Autorizacion = string.IsNullOrWhiteSpace(ret.Autorizacion)
                        ? preview.NumeroAutorizacion
                        : ret.Autorizacion,
                    IdRetencionInfo = retencionInfo?.Sec,
                    PorcentajeRetencion = ret.PorcentajeRetencion ?? ret.Valor ?? 0m,
                    ValorRetenido = ret.ValorRetenido ?? 0m
                };

                _context.ComprasRetValor.Add(item);
            }

            await _context.SaveChangesAsync();

            if (retencionInfo != null)
            {
                await _emisionControlService.ConsumirDocumentoAsync(_context, preview.Usuario);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { }
            throw;
        }
        });

        if (retencionInfo != null)
            await GenerarArchivosRetencionAsync(preview, retencionInfo);

        return compra!.CodFactura;
    }

    public async Task EliminarRetencionAsync(int sec)
    {
        var item = await _context.ComprasRetValor
            .FirstOrDefaultAsync(x => x.Sec == sec);

        if (item == null)
            throw new Exception("No se encontró la retención a eliminar.");

        _context.ComprasRetValor.Remove(item);
        await _context.SaveChangesAsync();
    }

    private async Task GenerarArchivosRetencionAsync(CompraXmlPreviewDto preview, RetencionInfo retencionInfo)
    {
        if (preview == null || retencionInfo == null || retencionInfo.Sec <= 0)
            return;

        preview.NumeroRetencionGenerado = string.IsNullOrWhiteSpace(retencionInfo.NumRetencion)
            ? preview.NumeroRetencionGenerado
            : retencionInfo.NumRetencion!;
        preview.ClaveAccesoRetencion = string.Empty;
        preview.NombreArchivoRetencion = string.Empty;

        await _retencionXmlGenerator.GenerarXmlDinamicamenteAsync(preview);

        if (!string.IsNullOrWhiteSpace(preview.ClaveAccesoRetencion))
            retencionInfo.Clave = preview.ClaveAccesoRetencion;

        if (!string.IsNullOrWhiteSpace(preview.NombreArchivoRetencion))
            retencionInfo.NombreXml = preview.NombreArchivoRetencion;

        await _context.SaveChangesAsync();

        var detalleRetencion = await _retencionGeneradaService.GetRetencionDetalleAsync(retencionInfo.Sec);
        if (detalleRetencion != null)
            await _retencionPdfService.GenerarPdfRetencionAsync(detalleRetencion);
    }

    private async Task ValidarRetencionAsync(string tipo, int? idRet, string? codigoRetencion)
    {
        tipo = tipo.Trim().ToUpperInvariant();
        var codigoTexto = (codigoRetencion ?? string.Empty).Trim();

        bool existe = tipo switch
        {
            "IVA" => idRet.HasValue && await _context.RetencionIva.AnyAsync(x => x.Codigo == idRet.Value),
            "RENTA" => !string.IsNullOrWhiteSpace(codigoTexto) && await _context.RetencionRenta.AnyAsync(x => x.Codigo == codigoTexto),
            _ => false
        };

        if (!existe)
            throw new Exception($"No existe la retención con código {(string.IsNullOrWhiteSpace(codigoTexto) ? idRet?.ToString() : codigoTexto)} para el tipo {tipo}.");
    }

    private static void ValidarRetencionesContraTotales(CompraXmlPreviewDto preview)
    {
        if (preview.Retenciones == null || !preview.Retenciones.Any())
            return;

        foreach (var ret in preview.Retenciones)
        {
            if (string.IsNullOrWhiteSpace(ret.Tipo))
                continue;

            var tipo = ret.Tipo.Trim().ToUpperInvariant();
            decimal valorRetenido = ret.ValorRetenido ?? 0m;
            decimal baseRet = ret.Base ?? 0m;

            if (valorRetenido < 0m)
                throw new Exception("No se permiten valores retenidos negativos.");

            if (baseRet < 0m)
                throw new Exception("No se permite base imponible negativa en retenciones.");

            if (valorRetenido > baseRet)
            {
                if (tipo == "RENTA")
                    throw new Exception("La retención de renta no puede ser mayor a la Base Imponible de renta.");

                if (tipo == "IVA")
                    throw new Exception("La retención de IVA no puede ser mayor a la Base Imponible de IVA.");

                throw new Exception("El valor retenido no puede ser mayor a la base imponible.");
            }

            if (tipo == "RENTA")
            {
                if (valorRetenido > preview.TotalSinImpuestos)
                    throw new Exception($"La retención de renta no puede ser mayor al Total sin Impuestos ({preview.TotalSinImpuestos:N2}).");

                if (baseRet > preview.TotalSinImpuestos)
                    throw new Exception($"La base de retención de renta no puede ser mayor al Total sin Impuestos ({preview.TotalSinImpuestos:N2}).");
            }
            else if (tipo == "IVA")
            {
                if (valorRetenido > preview.Iva)
                    throw new Exception($"La retención de IVA no puede ser mayor al IVA Total ({preview.Iva:N2}).");

                if (baseRet > preview.Iva)
                    throw new Exception($"La base de retención de IVA no puede ser mayor al IVA Total ({preview.Iva:N2}).");
            }
        }
    }

    private static void AcumularPorcentaje(
        CompraXmlPreviewDto dto,
        string codigoPorcentaje,
        string tarifaStr,
        decimal baseImp,
        decimal valor)
    {
        switch (ClasificarIva(codigoPorcentaje, tarifaStr))
        {
            case ClasificacionIva.Subtotal0:
                dto.Subtotal0 += baseImp;
                break;

            case ClasificacionIva.Subtotal5:
                dto.Subtotal5 += baseImp;
                dto.Iva5 += valor;
                dto.Iva += valor;
                break;

            case ClasificacionIva.Subtotal8:
                dto.Subtotal8 += baseImp;
                dto.Iva8 += valor;
                dto.Iva += valor;
                break;

            case ClasificacionIva.General:
                dto.Subtotal12 += baseImp;
                dto.Iva += valor;
                break;

            case ClasificacionIva.NoObjeto:
                dto.NoImp += baseImp;
                break;

            case ClasificacionIva.Exento:
                dto.ExIva += baseImp;
                break;
        }
    }

    public async Task<List<CompraXmlDetalleDto>> ObtenerProductosSugeridosPorProveedorAsync(string rucProveedor)
    {
        rucProveedor = (rucProveedor ?? "").Trim();

        if (string.IsNullOrWhiteSpace(rucProveedor))
            return new List<CompraXmlDetalleDto>();

        await using var context = await _dbFactory.CreateDbContextAsync();

        var proveedor = await context.Clientes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Numeroidentificacion == rucProveedor);

        if (proveedor == null)
            return new List<CompraXmlDetalleDto>();

        var comprasIds = await context.ComprasFacturas
            .AsNoTracking()
            .Where(x => x.Estado == true && x.CodClientes == proveedor.Codcliente)
            .OrderByDescending(x => x.CodFactura)
            .Take(10)
            .Select(x => x.CodFactura)
            .ToListAsync();

        if (!comprasIds.Any())
            return new List<CompraXmlDetalleDto>();

        var detalles = await context.ComprasDetalleFac
            .AsNoTracking()
            .Where(x => comprasIds.Contains(x.CodFactura))
            .OrderByDescending(x => x.CodLinea)
            .ToListAsync();

        return detalles
            .GroupBy(x => new
            {
                Codigo = x.CodPrincipal ?? "",
                Descripcion = x.DescripProducto ?? "",
                Precio = x.PrecioProducto ?? 0m,
                CodImp = x.CodImp ?? 0,
                PorImp = x.PorImp ?? 0,
                Tarifa = x.Tarifa ?? 0
            })
            .Select(g => g.First())
            .Select(d => new CompraXmlDetalleDto
            {
                CodPrincipal = d.CodPrincipal ?? "",
                CodAuxiliar = d.CodAuxiliar ?? "",
                Descripcion = d.DescripProducto ?? "",
                Cantidad = 1m,
                PrecioUnitario = d.PrecioProducto ?? 0m,
                Descuento = 0m,
                PrecioTotalSinImpuesto = d.PrecioProducto ?? 0m,
                CodImp = d.CodImp ?? 0,
                PorImp = d.PorImp ?? 0,
                Tarifa = d.Tarifa ?? 0,
                ValorIVA = 0m,
                ValorICE = 0m,
                ValorTotal = d.PrecioProducto ?? 0m
            })
            .ToList();
    }

    public CompraXmlDetalleDto CrearDetalleManualVacio()
    {
        return new CompraXmlDetalleDto
        {
            CodPrincipal = "",
            CodAuxiliar = "",
            Descripcion = "",
            Cantidad = 1m,
            PrecioUnitario = 0m,
            Descuento = 0m,
            PrecioTotalSinImpuesto = 0m,
            CodImp = 2,
            PorImp = 4,
            Tarifa = 15,
            ValorIVA = 0m,
            ValorICE = 0m,
            ValorTotal = 0m
        };
    }

    public void RecalcularDetalleManual(CompraXmlDetalleDto item)
    {
        if (item == null)
            return;

        item.Cantidad = Red2(item.Cantidad);
        item.PrecioUnitario = Red2(item.PrecioUnitario);
        item.Descuento = Red2(item.Descuento);
        item.ValorICE = Red2(item.ValorICE);

        if (item.Cantidad < 0) item.Cantidad = 0;
        if (item.PrecioUnitario < 0) item.PrecioUnitario = 0;
        if (item.Descuento < 0) item.Descuento = 0;
        if (item.ValorICE < 0) item.ValorICE = 0;

        item.PrecioTotalSinImpuesto = Red2((item.Cantidad * item.PrecioUnitario) - item.Descuento);

        if (item.PrecioTotalSinImpuesto < 0)
            item.PrecioTotalSinImpuesto = 0;

        decimal ivaLinea = 0m;

        if (item.CodImp == 2)
        {
            switch (item.Tarifa)
            {
                case 15:
                    ivaLinea = Red2(item.PrecioTotalSinImpuesto * 0.15m);
                    item.PorImp = 4;
                    break;

                case 5:
                    ivaLinea = Red2(item.PrecioTotalSinImpuesto * 0.05m);
                    item.PorImp = 5;
                    break;

                case 8:
                    ivaLinea = Red2(item.PrecioTotalSinImpuesto * 0.08m);
                    item.PorImp = 8;
                    break;

                default:
                    item.Tarifa = 0;
                    item.PorImp = 0;
                    ivaLinea = 0m;
                    break;
            }
        }
        else
        {
            item.CodImp = 2;
            item.Tarifa = 0;
            item.PorImp = 0;
            ivaLinea = 0m;
        }

        item.ValorIVA = Red2(ivaLinea);
        item.ValorTotal = Red2(item.PrecioTotalSinImpuesto + item.ValorIVA + item.ValorICE);
    }

    public void RecalcularTotalesManualDesdeDetalles(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            return;

        preview.Detalles ??= new List<CompraXmlDetalleDto>();

        preview.Subtotal0 = 0m;
        preview.Subtotal5 = 0m;
        preview.Subtotal8 = 0m;
        preview.Subtotal12 = 0m;
        preview.NoImp = 0m;
        preview.ExIva = 0m;
        preview.Iva = 0m;
        preview.Iva5 = 0m;
        preview.Iva8 = 0m;
        preview.TotalDescuento = 0m;

        foreach (var item in preview.Detalles)
        {
            RecalcularDetalleManual(item);

            preview.TotalDescuento += Red2(item.Descuento);

            switch (item.Tarifa)
            {
                case 0:
                    preview.Subtotal0 += Red2(item.PrecioTotalSinImpuesto);
                    break;

                case 5:
                    preview.Subtotal5 += Red2(item.PrecioTotalSinImpuesto);
                    preview.Iva5 += Red2(item.ValorIVA);
                    break;

                case 8:
                    preview.Subtotal8 += Red2(item.PrecioTotalSinImpuesto);
                    preview.Iva8 += Red2(item.ValorIVA);
                    break;

                case 15:
                    preview.Subtotal12 += Red2(item.PrecioTotalSinImpuesto);
                    break;

                default:
                    preview.Subtotal0 += Red2(item.PrecioTotalSinImpuesto);
                    break;
            }
        }

        preview.Subtotal0 = Red2(preview.Subtotal0);
        preview.Subtotal5 = Red2(preview.Subtotal5);
        preview.Subtotal8 = Red2(preview.Subtotal8);
        preview.Subtotal12 = Red2(preview.Subtotal12);
        preview.TotalDescuento = Red2(preview.TotalDescuento);

        preview.TotalSinImpuestos = Red2(
            preview.Subtotal0 +
            preview.Subtotal5 +
            preview.Subtotal8 +
            preview.Subtotal12 +
            preview.NoImp +
            preview.ExIva
        );

        var iva15 = Red2(preview.Subtotal12 * 0.15m);
        preview.Iva = Red2(preview.Iva5 + preview.Iva8 + iva15);

        preview.ImporteTotal = Red2(preview.TotalSinImpuestos - preview.TotalDescuento + preview.Iva);
    }

    public void ValidarCompraManual(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay información para validar.");

        if (string.IsNullOrWhiteSpace(preview.RucProveedor))
            throw new Exception("Debes ingresar el RUC o identificación del proveedor.");

        if (string.IsNullOrWhiteSpace(preview.RazonSocialProveedor))
            throw new Exception("Debes ingresar la razón social del proveedor.");

        if (string.IsNullOrWhiteSpace(preview.Secuencial))
            throw new Exception("Debes ingresar el secuencial de la compra.");

        if (string.IsNullOrWhiteSpace(preview.RucEmisor))
            throw new Exception("Debes ingresar el RUC del emisor.");

        if (!preview.CodEmisor.HasValue || preview.CodEmisor.Value <= 0)
            throw new Exception("No se encontró un emisor válido.");

        preview.Detalles ??= new List<CompraXmlDetalleDto>();

        if (preview.Detalles.Count > 0)
        {
            foreach (var item in preview.Detalles)
            {
                if (string.IsNullOrWhiteSpace(item.Descripcion))
                    throw new Exception("Todos los detalles deben tener descripción.");

                if (item.Cantidad <= 0)
                    throw new Exception("La cantidad de cada detalle debe ser mayor a 0.");

                if (item.PrecioUnitario < 0)
                    throw new Exception("El precio unitario no puede ser negativo.");
            }

            RecalcularTotalesManualDesdeDetalles(preview);
        }
        else
        {
            RecalcularTotalesManual(preview);
        }

        if (preview.TotalSinImpuestos < 0 || preview.ImporteTotal < 0)
            throw new Exception("Los totales calculados no son válidos.");
    }

    private static ClasificacionIva ClasificarIva(string codigoPorcentaje, string tarifaStr)
    {
        codigoPorcentaje = (codigoPorcentaje ?? "").Trim();
        decimal tarifa = ParseDecimal(tarifaStr);

        if (tarifa == 0m)
            return ClasificacionIva.Subtotal0;

        if (tarifa == 5m)
            return ClasificacionIva.Subtotal5;

        if (tarifa == 8m)
            return ClasificacionIva.Subtotal8;

        if (tarifa == 15m)
            return ClasificacionIva.General;

        switch (codigoPorcentaje)
        {
            case "0":
                return ClasificacionIva.Subtotal0;
            case "5":
                return ClasificacionIva.Subtotal5;
            case "8":
                return ClasificacionIva.Subtotal8;
            case "6":
                return ClasificacionIva.NoObjeto;
            case "7":
                return ClasificacionIva.Exento;
            case "2":
            case "3":
            case "4":
                return ClasificacionIva.General;
        }

        return ClasificacionIva.Subtotal0;
    }

    private static string GetCampoAdicional(XDocument doc, params string[] nombres)
    {
        var campos = doc.Descendants()
            .Where(x => x.Name.LocalName == "campoAdicional")
            .ToList();

        foreach (var nombre in nombres)
        {
            var valor = campos.FirstOrDefault(x =>
                string.Equals(
                    (string?)x.Attribute("nombre") ?? "",
                    nombre,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(valor))
                return valor;
        }

        return "";
    }

    private async Task<string> ObtenerDescripcionFormaPagoAsync(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return "";

        var forma = await _context.FormasPago
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo == codigo && x.Estado == true);

        if (forma == null)
            return codigo;

        return !string.IsNullOrWhiteSpace(forma.Descripcion)
            ? forma.Descripcion
            : codigo;
    }

    private async Task<string> ObtenerDescripcionTipoIdentificacionAsync(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return "";

        var tipo = await _context.Identificacion
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdeCodigo == codigo && x.Estado == true);

        if (tipo == null)
            return codigo;

        return !string.IsNullOrWhiteSpace(tipo.IdeDescripcion)
            ? tipo.IdeDescripcion
            : codigo;
    }

    private static string NormalizarTextoVacio(string? valor, string defecto = ".")
    {
        return string.IsNullOrWhiteSpace(valor) ? defecto : valor.Trim();
    }

    private static string? FormatearNumeroGuiaRemisionCompra(string? numeroGuia, string? estab = null, string? ptoEmi = null)
    {
        if (string.IsNullOrWhiteSpace(numeroGuia))
            return null;

        var limpio = new string((numeroGuia ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(limpio))
            return numeroGuia!.Trim();

        if (limpio.Length >= 14)
            return $"{limpio[..3]}-{limpio.Substring(3, 3)}-{limpio.Substring(6)}";

        var estabLimpio = new string((estab ?? string.Empty).Where(char.IsDigit).ToArray());
        var ptoEmiLimpio = new string((ptoEmi ?? string.Empty).Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(estabLimpio) || string.IsNullOrWhiteSpace(ptoEmiLimpio))
            return numeroGuia!.Trim();

        estabLimpio = estabLimpio.Length > 3 ? estabLimpio[^3..] : estabLimpio.PadLeft(3, '0');
        ptoEmiLimpio = ptoEmiLimpio.Length > 3 ? ptoEmiLimpio[^3..] : ptoEmiLimpio.PadLeft(3, '0');

        int longitudSecuencial = limpio.Length >= 9 ? 9 : 8;
        string secuencial = limpio.PadLeft(longitudSecuencial, '0');

        return $"{estabLimpio}-{ptoEmiLimpio}-{secuencial}";
    }

    private static string LimpiarTelefono(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? "." : valor.Trim();
    }

    public async Task<string> GenerarSecuenciaRetencionAsync(int? usuario = null, string? serie = null)
    {
        var query = _context.RetencionInfo
            .AsNoTracking()
            .Where(x => x.NumRetencion != null && x.NumRetencion != "");

        if (usuario.HasValue && usuario.Value > 0)
            query = query.Where(x => x.Usuario == usuario.Value);

        var registros = await query
            .Select(x => new { x.NumRetencion, x.Serie })
            .ToListAsync();

        var serieLimpia = LimpiarSerieRetencion(serie);
        if (!string.IsNullOrWhiteSpace(serieLimpia))
        {
            registros = registros
                .Where(x => LimpiarSerieRetencion(x.Serie) == serieLimpia)
                .ToList();
        }

        int maximo = 0;

        foreach (var registro in registros)
        {
            var soloNumero = new string((registro.NumRetencion ?? string.Empty).Where(char.IsDigit).ToArray());
            if (soloNumero.Length > 9)
                soloNumero = soloNumero[^9..];

            if (int.TryParse(soloNumero, out int valor) && valor > maximo)
                maximo = valor;
        }

        int siguiente = maximo + 1;
        return siguiente.ToString("D9");
    }

    private static string LimpiarSerieRetencion(string? serie)
    {
        var limpia = new string((serie ?? string.Empty).Where(char.IsDigit).ToArray());
        return limpia.Length > 6 ? limpia[..6] : limpia;
    }

    private static int ParseTarifaEntera(string? value)
    {
        var dec = ParseDecimal(value);
        return (int)decimal.Round(dec, 0, MidpointRounding.AwayFromZero);
    }

    private static string GetValue(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value?.Trim() ?? "";

    private static string GetFirstNonEmptyValue(XElement parent, string localName)
        => parent.Elements()
            .Where(x => x.Name.LocalName == localName)
            .Select(x => x.Value?.Trim() ?? "")
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        value = value.Replace(",", ".");
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result);
        return result;
    }

    private static int ParseInt(string? value)
    {
        int.TryParse(value, out var result);
        return result;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string[] formats =
        {
            "dd/MM/yyyy",
            "yyyy-MM-dd",
            "dd/MM/yyyy HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss"
        };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;

        if (DateTime.TryParse(value, out dt))
            return dt;

        return null;
    }

    private static char? ConvertirObligado(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        valor = valor.Trim().ToUpperInvariant();

        if (valor == "SI" || valor == "S" || valor == "1")
            return '1';

        if (valor == "NO" || valor == "N" || valor == "0")
            return '0';

        return null;
    }

    public async Task<List<CompraXmlBusquedaDto>> BuscarComprasXmlAsync(string filtro)
    {
        filtro = (filtro ?? "").Trim();

        var query = _context.ComprasFacturas
            .AsNoTracking()
            .Where(x => x.Estado == true);

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            query = query.Where(x =>
                (x.CodClave ?? "").Contains(filtro) ||
                (x.NumFactura ?? "").Contains(filtro) ||
                (x.Serie ?? "").Contains(filtro));
        }

        return await query
            .OrderByDescending(x => x.CodFactura)
            .Take(30)
            .Select(x => new CompraXmlBusquedaDto
            {
                CodFactura = x.CodFactura,
                ClaveAcceso = x.CodClave ?? "",
                RucProveedor = "",
                RazonSocialProveedor = "",
                Secuencial = x.NumFactura ?? "",
                Serie = x.Serie ?? "",
                FechaEmision = x.FchAutorizacion,
                Total = x.ValorTotal ?? 0m
            })
            .ToListAsync();
    }

    public async Task<List<string>> GetCorreosAdicionalesProveedorAsync(int codCliente)
    {
        if (codCliente <= 0)
            return new List<string>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.ClientesCorreos
            .AsNoTracking()
            .Where(cc => cc.CodCliente == codCliente && cc.Estado)
            .OrderBy(cc => cc.Id)
            .Select(cc => cc.Correo)
            .ToListAsync();
    }

    public async Task<CompraXmlPreviewDto?> BuscarPreviewPorSecuencialAsync(string secuencial)
    {
        secuencial = (secuencial ?? "").Trim();

        if (string.IsNullOrWhiteSpace(secuencial))
            return null;

        var limpio = new string(secuencial.Where(char.IsDigit).ToArray());
        var posibles = new List<string> { secuencial, limpio };

        if (limpio.Length > 9)
            posibles.Add(limpio[^9..]);

        if (int.TryParse(limpio, out int numeroSecuencial))
        {
            posibles.Add(numeroSecuencial.ToString());
            posibles.Add(numeroSecuencial.ToString("D2"));
            posibles.Add(numeroSecuencial.ToString("D3"));
            posibles.Add(numeroSecuencial.ToString("D4"));
            posibles.Add(numeroSecuencial.ToString("D5"));
            posibles.Add(numeroSecuencial.ToString("D6"));
            posibles.Add(numeroSecuencial.ToString("D7"));
            posibles.Add(numeroSecuencial.ToString("D8"));
            posibles.Add(numeroSecuencial.ToString("D9"));
        }

        posibles = posibles.Distinct().ToList();
        var serieLimpia = limpio.Length >= 6 ? limpio[..6] : string.Empty;
        var secuencialLimpio = limpio.Length > 6 ? limpio[6..] : limpio;

        var query = _context.ComprasFacturas
            .AsNoTracking()
            .Where(x =>
                x.Estado == true &&
                (x.CodDocumento == "03" || x.TipoDocumento == "LIQ"));

        ComprasFactura? compra = null;

        if (int.TryParse(limpio, out var codFacturaBusqueda))
        {
            compra = await query.FirstOrDefaultAsync(x => x.CodFactura == codFacturaBusqueda);
        }

        compra ??= await query.FirstOrDefaultAsync(x =>
            posibles.Contains(x.NumFactura ?? "") ||
            (x.CodClave ?? "") == secuencial ||
            (x.CodClave ?? "") == limpio ||
            (
                !string.IsNullOrWhiteSpace(serieLimpia) &&
                ((x.Serie ?? "").Replace("-", "") == serieLimpia) &&
                posibles.Contains(x.NumFactura ?? "")
            ) ||
            (
                !string.IsNullOrWhiteSpace(secuencialLimpio) &&
                ((x.Serie ?? "") + "-" + (x.NumFactura ?? "")) == secuencial
            ));

        if (compra == null)
            return null;

        var proveedor = compra.CodClientes.HasValue
            ? await _context.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codcliente == compra.CodClientes.Value)
            : null;

        var emisor = compra.CodEmisor.HasValue
            ? await _context.Emisores.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codigo == compra.CodEmisor.Value)
            : null;

        var detallesDb = await _context.ComprasDetalleFac
            .AsNoTracking()
            .Where(x => x.CodFactura == compra.CodFactura)
            .ToListAsync();

        var retencionesDb = await _context.ComprasRetValor
            .AsNoTracking()
            .Where(x => x.IdCompra == compra.CodFactura)
            .OrderBy(x => x.Sec)
            .ToListAsync();

        var tieneRetencionGenerada = await _context.RetencionInfo
            .AsNoTracking()
            .AnyAsync(x => x.IcCompra == compra.CodFactura);

        var preview = new CompraXmlPreviewDto
        {
            XmlOriginal = "",
            Usuario = compra.Usuario,
            ClaveAcceso = compra.CodClave ?? "",
            Estab = ObtenerEstabDesdeSerie(compra.Serie),
            PtoEmi = ObtenerPtoEmiDesdeSerie(compra.Serie),
            Secuencial = compra.NumFactura ?? "",
            RucProveedor = proveedor?.Numeroidentificacion ?? "",
            RazonSocialProveedor = proveedor?.Nombrerazonsocial ?? proveedor?.Nombrecomercial ?? "",
            DireccionProveedor = proveedor?.Direccion ?? "",
            TelefonoProveedor = proveedor?.Celular ?? "",
            TelefonoFijoProveedor = proveedor?.Telefonoconvencional ?? "",
            EmailProveedor = proveedor?.Correo ?? "",

            RucEmisor = emisor?.Ruc ?? "",
            IdentificacionComprador = emisor?.Ruc ?? "",

            TipoIdentificacionComprador = proveedor?.Tipoidentificacion ?? "",
            TipoIdentificacionCompradorNombre = await ObtenerDescripcionTipoIdentificacionAsync(proveedor?.Tipoidentificacion ?? ""),
            FechaEmision = compra.FchAutorizacion,
            FechaEmisionDocumentoSustento = compra.FchAutorizacion,
            TotalSinImpuestos = compra.Subtotal ?? 0m,
            TotalDescuento = compra.Descuentos ?? 0m,
            ImporteTotal = compra.ValorTotal ?? 0m,
            Moneda = "DOLAR",
            FormaPago = compra.TipoPago ?? "",
            FormaPagoNombre = await ObtenerDescripcionFormaPagoAsync(compra.TipoPago ?? ""),
            GuiaRemision = FormatearNumeroGuiaRemisionCompra(
                compra.GuiaRemision,
                ObtenerEstabDesdeSerie(compra.Serie),
                ObtenerPtoEmiDesdeSerie(compra.Serie)) ?? "",
            NumeroRetencionGenerado = string.IsNullOrWhiteSpace(compra.NumRetencion)
                ? await GenerarSecuenciaRetencionAsync(compra.Usuario, compra.Serie)
                : compra.NumRetencion!,
            NumeroAutorizacion = compra.NumAutorizacion ?? "",
            FechaAutorizacionSri = compra.FechaAutoSRI ?? "",
            Subtotal12 = compra.Subtotal12 ?? 0m,
            Subtotal0 = compra.Subtotal0 ?? 0m,
            Subtotal5 = compra.Subtotal5 ?? 0m,
            Subtotal8 = compra.Subtotal8 ?? 0m,
            NoImp = compra.NoImp ?? 0m,
            ExIva = compra.ExIva ?? 0m,
            Iva = compra.Iva ?? 0m,
            Iva5 = compra.Iva5 ?? 0m,
            Iva8 = compra.Iva8 ?? 0m,
            CodEmisor = compra.CodEmisor,
            NombreEmisorEncontrado = emisor != null ? $"{emisor.NomComercial} - {emisor.Ruc}" : "",
            CodProveedor = compra.CodClientes,
            YaImportado = true,
            CodFacturaExistente = compra.CodFactura,
            TieneRetencionGenerada = tieneRetencionGenerada,
            Detalles = detallesDb.Select(d => new CompraXmlDetalleDto
            {
                CodPrincipal = d.CodPrincipal ?? "",
                CodAuxiliar = d.CodAuxiliar ?? "",
                Descripcion = d.DescripProducto ?? "",
                Cantidad = d.CantProducto ?? 0m,
                PrecioUnitario = d.PrecioProducto ?? 0m,
                Descuento = d.Descuento ?? 0m,
                PrecioTotalSinImpuesto = d.ValorTProducto ?? 0m,
                CodImp = d.CodImp ?? 0,
                PorImp = d.PorImp ?? 0,
                Tarifa = d.Tarifa ?? 0,
                ValorIVA = d.ValorIVA ?? 0m,
                ValorICE = d.ValorICE ?? 0m,
                ValorTotal = d.ValorTotal ?? 0m
            }).ToList(),
            Retenciones = new List<CompraRetValorDto>()
        };

        await AplicarEmisorConfiguradoAsync(preview);

        foreach (var r in retencionesDb)
        {
            string descripcion = "";

            if ((r.Tipo ?? "").Trim().ToUpperInvariant() == "IVA" && r.IdRet.HasValue)
            {
                var iva = await _context.RetencionIva.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Codigo == r.IdRet.Value);
                descripcion = iva?.Descripcion ?? "";
            }
            else if ((r.Tipo ?? "").Trim().ToUpperInvariant() == "RENTA" && r.IdRet.HasValue)
            {
                var renta = await _context.RetencionRenta.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Codigo == r.IdRet.Value.ToString());
                descripcion = renta?.Descripcion ?? "";
            }

            preview.Retenciones.Add(new CompraRetValorDto
            {
                Sec = r.Sec,
                IdRet = r.IdRet,
                Valor = r.Valor,
                Base = r.Base,
                Tipo = r.Tipo,
                Estado = r.Estado,
                Serie = r.Serie,
                NumSri = r.NumSri,
                Autorizacion = r.Autorizacion,
                IdRetencionInfo = r.IdRetencionInfo,
                PorcentajeRetencion = r.PorcentajeRetencion,
                ValorRetenido = r.ValorRetenido,
                DescripcionRet = descripcion
            });
        }

        return preview;
    }

    public async Task AplicarEmisorConfiguradoAsync(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            return;

        var emisor = await BuscarEmisorConfiguradoAsync(preview.CodEmisor, preview.RucEmisor, preview.Usuario);
        if (emisor == null)
            return;

        preview.CodEmisor = emisor.Codigo;
        preview.RucEmisor = string.IsNullOrWhiteSpace(emisor.Ruc) ? (preview.RucEmisor ?? "") : emisor.Ruc.Trim();
        preview.IdentificacionComprador = preview.RucEmisor;
        preview.NombreEmisorEncontrado = $"{(string.IsNullOrWhiteSpace(emisor.NomComercial) ? emisor.RazonSocial : emisor.NomComercial)} - {emisor.Ruc}";

        if (string.IsNullOrWhiteSpace(preview.DireccionMatriz))
            preview.DireccionMatriz = emisor.DireccionMatriz?.Trim() ?? emisor.DirEstablecimiento?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(preview.DireccionEstablecimiento))
            preview.DireccionEstablecimiento = emisor.DirEstablecimiento?.Trim() ?? emisor.DireccionMatriz?.Trim() ?? "";

        preview.ObligadoContabilidad = NormalizarObligadoContabilidad(emisor.LlevaContabilidad);
    }

    private async Task<Emisor?> BuscarEmisorConfiguradoAsync(int? codEmisor, string? rucEmisor, int? idUsuario)
    {
        Emisor? emisor = null;

        if (codEmisor.HasValue && codEmisor.Value > 0)
        {
            emisor = await _context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codigo == codEmisor.Value);
        }

        if (emisor == null && !string.IsNullOrWhiteSpace(rucEmisor))
        {
            emisor = await _context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Ruc == rucEmisor);
        }

        if (emisor != null)
            return emisor;

        var usuarioEmisor = await ResolveEmisorOwnerUserIdAsync(idUsuario);

        if (usuarioEmisor.HasValue)
        {
            emisor = await _context.Emisores
                .AsNoTracking()
                .Where(x => x.Estado == true && x.IdUsuario == usuarioEmisor.Value)
                .OrderByDescending(x => x.Codigo)
                .FirstOrDefaultAsync();
        }

        if (emisor == null && codEmisor.HasValue && codEmisor.Value > 0)
        {
            emisor = await _context.Emisores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Codigo == codEmisor.Value);
        }

        return emisor;
    }

    private async Task<int?> ResolveEmisorOwnerUserIdAsync(int? idUsuario)
    {
        if (!idUsuario.HasValue || idUsuario.Value <= 0)
            return idUsuario;

        var usuario = await _context.Usuarios
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == idUsuario.Value);

        if (usuario == null)
            return idUsuario;

        return usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : idUsuario.Value;
    }

    private static string NormalizarObligadoContabilidad(string? valor)
    {
        var normalizado = (valor ?? string.Empty).Trim().ToUpperInvariant();
        return normalizado is "SI" or "NO" ? normalizado : "NO";
    }

    private static string ObtenerEstabDesdeSerie(string? serie)
    {
        if (string.IsNullOrWhiteSpace(serie))
            return "";

        serie = serie.Trim();
        return serie.Length >= 3 ? serie.Substring(0, 3) : serie;
    }

    private static string ObtenerPtoEmiDesdeSerie(string? serie)
    {
        if (string.IsNullOrWhiteSpace(serie))
            return "";

        serie = serie.Trim();
        return serie.Length >= 6 ? serie.Substring(3, 3) : "";
    }

    private decimal ObtenerIva15DesdeTotales(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            return 0m;

        return Red2(preview.Iva - preview.Iva5 - preview.Iva8);
    }

    private enum ClasificacionIva
    {
        General = 0,
        Subtotal0 = 1,
        Subtotal5 = 2,
        Subtotal8 = 3,
        NoObjeto = 4,
        Exento = 5
    }
}
