using System.Xml.Linq;
using System.Text;
using Simetric.DTOs;
using System.Globalization;

namespace Simetric.Services;

public class ComprobanteRetencionGenerator
{
    public async Task<string> GenerarXmlDinamicamenteAsync(CompraXmlPreviewDto preview)
    {
        if (preview == null)
            throw new Exception("No hay datos para generar el XML.");

        string estabRet = NormalizarSegmento(preview.Estab, 3, "001");
        string ptoEmiRet = NormalizarSegmento(preview.PtoEmi, 3, "001");
        DateTime fechaEmision = preview.FechaEmision ?? DateTime.Today;
        DateTime fechaDocumentoSustento = preview.FechaEmisionDocumentoSustento ?? fechaEmision;
        string rucEmisor = NormalizarSoloDigitos(preview.RucEmisor, string.Empty);
        if (rucEmisor.Length != 13)
            throw new InvalidOperationException("El RUC del emisor debe tener 13 dígitos para generar la retención.");

        string secuencialRet = NormalizarSecuencial(preview.NumeroRetencionGenerado, 9, "000000001");

        string codigoNumerico = "12345678";

        string claveAcceso = string.IsNullOrWhiteSpace(preview.ClaveAccesoRetencion)
            ? GenerarClaveAcceso(
                fechaEmision,
                "07",
                rucEmisor,
                preview.Ambiente.ToString(),
                estabRet + ptoEmiRet,
                secuencialRet,
                codigoNumerico,
                "1")
            : NormalizarSoloDigitos(preview.ClaveAccesoRetencion, string.Empty);

        string nombreArchivo = string.IsNullOrWhiteSpace(preview.NombreArchivoRetencion)
            ? $"RET_{claveAcceso}.xml"
            : preview.NombreArchivoRetencion;

        preview.ClaveAccesoRetencion = claveAcceso;
        preview.NombreArchivoRetencion = nombreArchivo;
        preview.NumeroRetencionGenerado = secuencialRet;

        var codigoDocumentoSustento = ResolverCodigoDocumentoSustento(preview);
        var numeroDocumentoSustento = NormalizarNumeroDocumentoSustento(preview);
        var numeroAutorizacionSustento = NormalizarAutorizacion(preview.NumeroAutorizacion, preview.ClaveAcceso);
        var tipoIdentificacion = NormalizarTipoIdentificacion(
            preview.TipoIdentificacionComprador,
            preview.RucProveedor);
        var impuestosDocSustento = ConstruirImpuestosDocSustento(preview);

        XDocument doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("comprobanteRetencion",
                new XAttribute("id", "comprobante"),
                new XAttribute("version", "2.0.0"),

                new XElement("infoTributaria",
                    new XElement("ambiente", preview.Ambiente),
                    new XElement("tipoEmision", "1"),
                    new XElement("razonSocial", preview.NombreEmisorEncontrado),
                    string.IsNullOrWhiteSpace(preview.NombreEmisorEncontrado)
                        ? null
                        : new XElement("nombreComercial", preview.NombreEmisorEncontrado.Trim()),
                    new XElement("ruc", rucEmisor),
                    new XElement("claveAcceso", claveAcceso),
                    new XElement("codDoc", "07"),
                    new XElement("estab", estabRet),
                    new XElement("ptoEmi", ptoEmiRet),
                    new XElement("secuencial", secuencialRet),
                    new XElement("dirMatriz", preview.DireccionMatriz)
                ),

                new XElement("infoCompRetencion",
                    new XElement("fechaEmision", fechaEmision.ToString("dd/MM/yyyy")),
                    new XElement("dirEstablecimiento", string.IsNullOrWhiteSpace(preview.DireccionEstablecimiento) ? (preview.DireccionMatriz ?? "") : preview.DireccionEstablecimiento),
                    new XElement("obligadoContabilidad", string.IsNullOrWhiteSpace(preview.ObligadoContabilidad) ? "NO" : preview.ObligadoContabilidad),
                    new XElement("tipoIdentificacionSujetoRetenido", tipoIdentificacion),
                    new XElement("parteRel", "NO"),
                    new XElement("razonSocialSujetoRetenido", preview.RazonSocialProveedor ?? ""),
                    new XElement("identificacionSujetoRetenido", preview.RucProveedor ?? ""),
                    new XElement("periodoFiscal", fechaEmision.ToString("MM/yyyy"))
                ),

                new XElement("docsSustento",
                    new XElement("docSustento",
                        new XElement("codSustento", "01"),
                        new XElement("codDocSustento", codigoDocumentoSustento),
                        new XElement("numDocSustento", numeroDocumentoSustento),
                        new XElement("fechaEmisionDocSustento",
                            fechaDocumentoSustento.ToString("dd/MM/yyyy")
                        ),
                        string.IsNullOrWhiteSpace(numeroAutorizacionSustento)
                            ? null
                            : new XElement("numAutDocSustento", numeroAutorizacionSustento),
                        new XElement("pagoLocExt", "01"),
                        new XElement("totalSinImpuestos",
                            preview.TotalSinImpuestos.ToString("F2", CultureInfo.InvariantCulture)
                        ),
                        new XElement("importeTotal",
                            preview.ImporteTotal.ToString("F2", CultureInfo.InvariantCulture)
                        ),

                        new XElement("impuestosDocSustento", impuestosDocSustento),

                        new XElement("retenciones",
                            (preview.Retenciones ?? new List<CompraRetValorDto>())
                                .Select(r => new XElement("retencion",
                                    new XElement("codigo", ((r.Tipo ?? "").Trim().ToUpperInvariant()) == "IVA" ? "2" : "1"),
                                    new XElement("codigoRetencion", string.IsNullOrWhiteSpace(r.CodigoRetencion) ? (r.IdRet?.ToString() ?? "0") : r.CodigoRetencion),
                                    new XElement("baseImponible", (r.Base ?? 0m).ToString("F2", CultureInfo.InvariantCulture)),
                                    new XElement("porcentajeRetener", (r.PorcentajeRetencion ?? 0m).ToString("F2", CultureInfo.InvariantCulture)),
                                    new XElement("valorRetenido", (r.ValorRetenido ?? 0m).ToString("F2", CultureInfo.InvariantCulture))
                                ))
                        ),

                        new XElement("pagos",
                            new XElement("pago",
                                new XElement("formaPago", NormalizarFormaPago(preview.FormaPago)),
                                new XElement("total", preview.ImporteTotal.ToString("F2", CultureInfo.InvariantCulture))
                            )
                        )
                    )
                )
            )
        );

        return await GuardarArchivoXml(doc, nombreArchivo);
    }

    private static string NormalizarSegmento(string? valor, int longitud, string fallback)
    {
        var limpio = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(limpio))
            limpio = fallback;

        return limpio.PadLeft(longitud, '0')[^longitud..];
    }

    private static string NormalizarSecuencial(string? valor, int longitud, string fallback)
    {
        var limpio = NormalizarSoloDigitos(valor, fallback);
        return limpio.Length > longitud
            ? limpio[^longitud..]
            : limpio.PadLeft(longitud, '0');
    }

    private static string NormalizarSoloDigitos(string? valor, string fallback)
    {
        var limpio = new string((valor ?? string.Empty).Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(limpio) ? fallback : limpio;
    }

    private static string ResolverCodigoDocumentoSustento(CompraXmlPreviewDto preview)
    {
        var clave = NormalizarSoloDigitos(preview.ClaveAcceso, string.Empty);
        return clave.Length == 49 ? clave.Substring(8, 2) : "03";
    }

    private static string NormalizarNumeroDocumentoSustento(CompraXmlPreviewDto preview)
    {
        var serie = NormalizarSoloDigitos(preview.Serie, string.Empty);
        var secuencial = NormalizarSecuencial(preview.Secuencial, 9, "000000001");
        var numero = serie + secuencial;

        if (serie.Length != 6 || numero.Length != 15)
            throw new InvalidOperationException("El número del documento sustento debe contener establecimiento, punto de emisión y secuencial.");

        return numero;
    }

    private static string NormalizarAutorizacion(params string?[] valores)
    {
        foreach (var valor in valores)
        {
            var limpio = NormalizarSoloDigitos(valor, string.Empty);
            if (limpio.Length is 10 or 37 or 49)
                return limpio;
        }

        return string.Empty;
    }

    private static string NormalizarTipoIdentificacion(string? tipo, string? identificacion)
    {
        var codigo = NormalizarSoloDigitos(tipo, string.Empty);
        if (codigo.Length == 2)
            return codigo;

        var id = NormalizarSoloDigitos(identificacion, string.Empty);
        return id.Length switch
        {
            13 => "04",
            10 => "05",
            _ => "06"
        };
    }

    private static string NormalizarFormaPago(string? formaPago)
    {
        var codigo = NormalizarSoloDigitos(formaPago, "20");
        return codigo.Length > 2 ? codigo[^2..] : codigo.PadLeft(2, '0');
    }

    private static IEnumerable<XElement> ConstruirImpuestosDocSustento(CompraXmlPreviewDto preview)
    {
        var impuestos = new List<XElement>();
        var iva15 = Math.Max(0m, preview.Iva - preview.Iva5 - preview.Iva8);

        AgregarImpuesto(impuestos, "4", preview.Subtotal12, 15m, iva15);
        AgregarImpuesto(impuestos, "5", preview.Subtotal5, 5m, preview.Iva5);
        AgregarImpuesto(impuestos, "8", preview.Subtotal8, 8m, preview.Iva8);
        AgregarImpuesto(impuestos, "0", preview.Subtotal0, 0m, 0m);
        AgregarImpuesto(impuestos, "6", preview.NoImp, 0m, 0m);
        AgregarImpuesto(impuestos, "7", preview.ExIva, 0m, 0m);

        if (impuestos.Count == 0)
            AgregarImpuesto(impuestos, "0", preview.TotalSinImpuestos, 0m, 0m, incluirBaseCero: true);

        return impuestos;
    }

    private static void AgregarImpuesto(
        ICollection<XElement> impuestos,
        string codigoPorcentaje,
        decimal baseImponible,
        decimal tarifa,
        decimal valorImpuesto,
        bool incluirBaseCero = false)
    {
        if (!incluirBaseCero && baseImponible <= 0m)
            return;

        impuestos.Add(new XElement("impuestoDocSustento",
            new XElement("codImpuestoDocSustento", "2"),
            new XElement("codigoPorcentaje", codigoPorcentaje),
            new XElement("baseImponible", baseImponible.ToString("F2", CultureInfo.InvariantCulture)),
            new XElement("tarifa", tarifa.ToString("F2", CultureInfo.InvariantCulture)),
            new XElement("valorImpuesto", valorImpuesto.ToString("F2", CultureInfo.InvariantCulture))));
    }

    private async Task<string> GuardarArchivoXml(XDocument doc, string nombreArchivo)
    {
        string ruta = Path.Combine(
            Directory.GetCurrentDirectory(),
            "wwwroot",
            "comprobantes",
            "generados"
        );

        if (!Directory.Exists(ruta))
            Directory.CreateDirectory(ruta);

        string rutaCompleta = Path.Combine(ruta, nombreArchivo);

        await Task.Run(() => doc.Save(rutaCompleta));

        return nombreArchivo;
    }

    public string GenerarClaveAcceso(
        DateTime fecha,
        string tipoCompro,
        string ruc,
        string ambiente,
        string serie,
        string secuencial,
        string codigoNum,
        string emision)
    {
        StringBuilder clave = new StringBuilder();

        clave.Append(fecha.ToString("ddMMyyyy"));
        clave.Append(tipoCompro.PadLeft(2, '0'));
        clave.Append((ruc ?? "").PadLeft(13, '0'));
        clave.Append((ambiente ?? "").PadLeft(1, '0'));
        clave.Append((serie ?? "").Replace("-", "").PadLeft(6, '0'));
        clave.Append((secuencial ?? "").PadLeft(9, '0'));
        clave.Append((codigoNum ?? "").PadLeft(8, '0'));
        clave.Append((emision ?? "").PadLeft(1, '0'));

        int verificador = CalcularModulo11(clave.ToString());
        clave.Append(verificador);

        return clave.ToString();
    }

    private int CalcularModulo11(string cadena)
    {
        int suma = 0;
        int factor = 2;

        for (int i = cadena.Length - 1; i >= 0; i--)
        {
            suma += (int)char.GetNumericValue(cadena[i]) * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        int residuo = suma % 11;
        int resultado = 11 - residuo;

        if (resultado == 11) return 0;
        if (resultado == 10) return 1;
        return resultado;
    }
}
