using Simetric.Models;

namespace Simetric.DTOs;

public class RetencionGeneradaListDto
{
    public int Sec { get; set; }

    public string NumeroRetencion { get; set; } = "";
    public string Serie { get; set; } = "";
    public string NumeroCompleto => FormatearDocumento(Serie, NumeroRetencion);

    public string TipoIdentificacionProveedor { get; set; } = "";

    public DateTime? Fecha { get; set; }

    public string Proveedor { get; set; } = "";
    public string IdentificacionProveedor { get; set; } = "";

    public string DocumentoSustento { get; set; } = "";
    public string Clave { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public string Autorizado { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Mensaje { get; set; } = "";

    public decimal BaseTotal { get; set; }
    public decimal TotalRetenido { get; set; }

    public string XmlUrl { get; set; } = "";

    private static string FormatearSerie(string? serie)
    {
        var limpio = (serie ?? "").Replace("-", "").Trim();

        if (limpio.Length == 6)
            return $"{limpio[..3]}-{limpio.Substring(3, 3)}";

        return serie ?? "";
    }

    private static string FormatearDocumento(string? serie, string? numero)
    {
        var numeroLimpio = new string((numero ?? string.Empty).Where(char.IsDigit).ToArray());
        if (numeroLimpio.Length > 9)
            numeroLimpio = numeroLimpio[^9..];

        var secuencial = string.IsNullOrWhiteSpace(numeroLimpio) ? "000000000" : numeroLimpio.PadLeft(9, '0');
        var serieVisual = FormatearSerie(serie);

        return string.IsNullOrWhiteSpace(serieVisual) ? secuencial : $"{serieVisual}-{secuencial}";
    }
}

public class RetencionGeneradaDetalleViewDto
{
    public RetencionInfo RetencionInfo { get; set; } = new();
    public ComprasFactura? Compra { get; set; }
    public Proveedor? Proveedor { get; set; }
    public Emisor? Emisor { get; set; }
    public string TipoIdentificacionProveedor { get; set; } = "";

    public string NumeroCompleto { get; set; } = "";
    public string DocumentoSustentoVisual { get; set; } = "";
    public DateTime? FechaEmisionDocumentoSustento { get; set; }
    public string XmlUrl { get; set; } = "";

    public decimal BaseTotal { get; set; }
    public decimal TotalRetenido { get; set; }

    public List<RetencionGeneradaDetalleLineaDto> Retenciones { get; set; } = new();
}

public class RetencionGeneradaDetalleLineaDto
{
    public string Tipo { get; set; } = "";
    public string CodigoRetencion { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal BaseImponible { get; set; }
    public decimal PorcentajeRetener { get; set; }
    public decimal ValorRetenido { get; set; }
}
