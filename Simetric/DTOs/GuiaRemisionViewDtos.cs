using Simetric.Models;

namespace Simetric.DTOs;

public class GuiaRemisionListDto
{
    public int Sec { get; set; }
    public string NumeroGuiaRemision { get; set; } = "";
    public string Serie { get; set; } = "";
    public string NumeroCompleto => $"{FormatearSerie(Serie)}-{NumeroGuiaRemision.PadLeft(9, '0')}";
    public DateTime? FechaEmision { get; set; }
    public DateTime? FechaInicioTransporte { get; set; }
    public DateTime? FechaFinTransporte { get; set; }
    public string Destinatario { get; set; } = "";
    public string IdentificacionDestinatario { get; set; } = "";
    public string Transportista { get; set; } = "";
    public string FacturaSustento { get; set; } = "";
    public string MotivoTraslado { get; set; } = "";
    public string EstadoSri { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public string FechaAutorizacion { get; set; } = "";
    public string ClaveAcceso { get; set; } = "";
    public string XmlUrl { get; set; } = "";
    public string PdfUrl { get; set; } = "";

    private static string FormatearSerie(string? serie)
    {
        var limpio = (serie ?? "").Replace("-", "").Trim();
        if (limpio.Length == 6)
            return $"{limpio[..3]}-{limpio.Substring(3, 3)}";

        return serie ?? "";
    }
}

public class GuiaRemisionDetalleViewDto
{
    public GuiaRemision Guia { get; set; } = new();
    public GuiaDestinatario? Destinatario { get; set; }
    public Transportista? Transportista { get; set; }
    public Emisor? Emisor { get; set; }
    public Factura? Factura { get; set; }

    public string NumeroCompleto { get; set; } = "";
    public string NumeroDocumentoSustentoVisual { get; set; } = "";
    public string XmlUrl { get; set; } = "";
    public string PdfUrl { get; set; } = "";

    public List<GuiaRemisionDetalleLineaDto> Detalles { get; set; } = new();
}

public class GuiaRemisionDetalleLineaDto
{
    public string CodigoInterno { get; set; } = "";
    public string CodigoAdicional { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
}
