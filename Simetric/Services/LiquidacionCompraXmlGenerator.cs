using Simetric.DTOs;
using System.Globalization;
using System.Xml.Linq;

namespace Simetric.Services;

public class LiquidacionCompraXmlGenerator
{
    public async Task<string> GenerarXmlTemporalAsync(LiquidacionCompraPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay datos para generar el XML de la liquidacion.");

        if (string.IsNullOrWhiteSpace(preview.ClaveAcceso))
            throw new Exception("La liquidacion no tiene clave de acceso temporal.");

        var cultura = CultureInfo.InvariantCulture;
        var secuencial = (preview.Secuencial ?? "1").PadLeft(9, '0');
        var direccionEmisor = string.IsNullOrWhiteSpace(preview.DireccionEstablecimiento)
            ? (preview.DireccionMatriz ?? "")
            : preview.DireccionEstablecimiento;

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("liquidacionCompra",
                new XAttribute("id", "comprobante"),
                new XAttribute("version", "1.1.0"),
                new XElement("infoTributaria",
                    new XElement("ambiente", preview.Ambiente),
                    new XElement("tipoEmision", "1"),
                    new XElement("razonSocial", preview.RazonSocialEmisor),
                    new XElement("nombreComercial", string.IsNullOrWhiteSpace(preview.NombreComercialEmisor) ? preview.RazonSocialEmisor : preview.NombreComercialEmisor),
                    new XElement("ruc", preview.RucEmisor),
                    new XElement("claveAcceso", preview.ClaveAcceso),
                    new XElement("codDoc", "03"),
                    new XElement("estab", preview.Estab),
                    new XElement("ptoEmi", preview.PtoEmi),
                    new XElement("secuencial", secuencial),
                    new XElement("dirMatriz", direccionEmisor)
                ),
                new XElement("infoLiquidacionCompra",
                    new XElement("fechaEmision", (preview.FechaEmision ?? DateTime.Today).ToString("dd/MM/yyyy", cultura)),
                    new XElement("dirEstablecimiento", direccionEmisor),
                    string.IsNullOrWhiteSpace(preview.ContribuyenteEspecial)
                        ? null
                        : new XElement("contribuyenteEspecial", preview.ContribuyenteEspecial),
                    new XElement("obligadoContabilidad", string.IsNullOrWhiteSpace(preview.ObligadoContabilidad) ? "NO" : preview.ObligadoContabilidad),
                    new XElement("tipoIdentificacionProveedor", preview.TipoIdentificacionProveedor),
                    new XElement("razonSocialProveedor", preview.RazonSocialProveedor),
                    new XElement("identificacionProveedor", preview.IdentificacionProveedor),
                    string.IsNullOrWhiteSpace(preview.DireccionProveedor)
                        ? null
                        : new XElement("direccionProveedor", preview.DireccionProveedor),
                    new XElement("totalSinImpuestos", preview.TotalSinImpuestos.ToString("F2", cultura)),
                    new XElement("totalDescuento", preview.TotalDescuento.ToString("F2", cultura)),
                    new XElement("totalConImpuestos", ConstruirTotalesImpuestos(preview, cultura)),
                    new XElement("importeTotal", preview.ImporteTotal.ToString("F2", cultura)),
                    new XElement("moneda", string.IsNullOrWhiteSpace(preview.Moneda) ? "DOLAR" : preview.Moneda),
                    new XElement("pagos",
                        new XElement("pago",
                            new XElement("formaPago", string.IsNullOrWhiteSpace(preview.FormaPago) ? "20" : preview.FormaPago),
                            new XElement("total", preview.ImporteTotal.ToString("F2", cultura)),
                            preview.Plazo.HasValue && preview.Plazo.Value > 0
                                ? new XElement("plazo", preview.Plazo.Value.ToString(cultura))
                                : null,
                            preview.Plazo.HasValue && preview.Plazo.Value > 0 && !string.IsNullOrWhiteSpace(preview.UnidadTiempo)
                                ? new XElement("unidadTiempo", preview.UnidadTiempo)
                                : null
                        )
                    )
                ),
                new XElement("detalles",
                    (preview.Detalles ?? new List<LiquidacionCompraDetalleDto>())
                        .Select(x => ConstruirDetalle(x, cultura))
                ),
                ConstruirInfoAdicional(preview)
            )
        );

        var carpeta = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "comprobantes",
            "liquidaciones");

        if (!Directory.Exists(carpeta))
            Directory.CreateDirectory(carpeta);

        var nombreArchivo = $"LIQ_{preview.ClaveAcceso}.xml";
        var rutaCompleta = Path.Combine(carpeta, nombreArchivo);

        await Task.Run(() => doc.Save(rutaCompleta));
        return nombreArchivo;
    }

    private static IEnumerable<XElement> ConstruirTotalesImpuestos(LiquidacionCompraPreviewDto preview, CultureInfo cultura)
    {
        var items = new List<XElement>();

        AgregarTotalImpuesto(items, preview.Subtotal0, 0, 0m, cultura);
        AgregarTotalImpuesto(items, preview.Subtotal5, 5, preview.Iva5, cultura);
        AgregarTotalImpuesto(items, preview.Subtotal8, 8, preview.Iva8, cultura);
        AgregarTotalImpuesto(items, preview.Subtotal15, 4, preview.Iva15, cultura);
        AgregarTotalImpuesto(items, preview.NoImp, 6, 0m, cultura);
        AgregarTotalImpuesto(items, preview.ExIva, 7, 0m, cultura);

        return items;
    }

    private static void AgregarTotalImpuesto(List<XElement> items, decimal baseImponible, int codigoPorcentaje, decimal valor, CultureInfo cultura)
    {
        if (baseImponible <= 0m)
            return;

        items.Add(
            new XElement("totalImpuesto",
                new XElement("codigo", "2"),
                new XElement("codigoPorcentaje", codigoPorcentaje.ToString(cultura)),
                new XElement("baseImponible", baseImponible.ToString("F2", cultura)),
                new XElement("tarifa", ObtenerTarifaDesdeCodigo(codigoPorcentaje).ToString("F2", cultura)),
                new XElement("valor", valor.ToString("F2", cultura))
            ));
    }

    private static XElement ConstruirDetalle(LiquidacionCompraDetalleDto item, CultureInfo cultura)
    {
        return new XElement("detalle",
            new XElement("codigoPrincipal", string.IsNullOrWhiteSpace(item.CodPrincipal) ? "SIN-CODIGO" : item.CodPrincipal),
            string.IsNullOrWhiteSpace(item.CodAuxiliar)
                ? null
                : new XElement("codigoAuxiliar", item.CodAuxiliar),
            new XElement("descripcion", item.Descripcion),
            new XElement("cantidad", item.Cantidad.ToString("F6", cultura)),
            new XElement("precioUnitario", item.PrecioUnitario.ToString("F6", cultura)),
            new XElement("descuento", item.Descuento.ToString("F2", cultura)),
            new XElement("precioTotalSinImpuesto", item.PrecioTotalSinImpuesto.ToString("F2", cultura)),
            new XElement("impuestos",
                new XElement("impuesto",
                    new XElement("codigo", "2"),
                    new XElement("codigoPorcentaje", item.CodigoPorcentaje.ToString(cultura)),
                    new XElement("tarifa", ObtenerTarifaDesdeCodigo(item.CodigoPorcentaje).ToString("F2", cultura)),
                    new XElement("baseImponible", item.PrecioTotalSinImpuesto.ToString("F2", cultura)),
                    new XElement("valor", item.ValorIva.ToString("F2", cultura))
                )
            )
        );
    }

    private static XElement? ConstruirInfoAdicional(LiquidacionCompraPreviewDto preview)
    {
        var campos = new List<XElement>();

        if (!string.IsNullOrWhiteSpace(preview.TelefonoProveedor))
            campos.Add(new XElement("campoAdicional", new XAttribute("nombre", "TelefonoProveedor"), preview.TelefonoProveedor));

        if (!string.IsNullOrWhiteSpace(preview.EmailProveedor))
            campos.Add(new XElement("campoAdicional", new XAttribute("nombre", "EmailProveedor"), preview.EmailProveedor));

        campos.Add(new XElement("campoAdicional", new XAttribute("nombre", "OrigenRegistro"), "REGISTRO MANUAL"));

        if (!campos.Any())
            return null;

        return new XElement("infoAdicional", campos);
    }

    private static decimal ObtenerTarifaDesdeCodigo(int codigoPorcentaje)
    {
        return codigoPorcentaje switch
        {
            5 => 5m,
            8 => 8m,
            4 => 15m,
            _ => 0m
        };
    }
}
