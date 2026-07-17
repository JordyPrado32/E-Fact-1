using Simetric.Models;

namespace Simetric.DTOs;

public class LiquidacionCompraListDto
{
    public int CodFactura { get; set; }
    public string Serie { get; set; } = "";
    public string Secuencial { get; set; } = "";
    public string NumeroDocumento => $"{SerieVisual}-{Secuencial.PadLeft(9, '0')}";
    public string SerieVisual => FormatearSerie(Serie);
    public DateTime? FechaEmision { get; set; }
    public string Proveedor { get; set; } = "";
    public string IdentificacionProveedor { get; set; } = "";
    public string EstadoSri { get; set; } = "";
    public string Autorizado { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public string MensajeSri { get; set; } = "";
    public decimal TotalSinImpuestos { get; set; }
    public decimal IvaTotal { get; set; }
    public decimal ImporteTotal { get; set; }
    public string ClaveAcceso { get; set; } = "";
    public string XmlUrl { get; set; } = "";
    public string PdfUrl { get; set; } = "";
    public bool TieneRetencion { get; set; }
    public string NumeroRetencion { get; set; } = "";

    private static string FormatearSerie(string? serie)
    {
        var limpio = (serie ?? "").Replace("-", "").Trim();
        if (limpio.Length == 6)
            return $"{limpio[..3]}-{limpio.Substring(3, 3)}";

        return serie ?? "";
    }
}

public class LiquidacionCompraDetalleViewDto
{
    public int CodFactura { get; set; }
    public ComprasFactura Compra { get; set; } = new();
    public Cliente? Proveedor { get; set; }
    public Emisor? Emisor { get; set; }
    public LiquidacionCompraPreviewDto Preview { get; set; } = new();
    public string XmlUrl { get; set; } = "";
    public string PdfUrl { get; set; } = "";
}
