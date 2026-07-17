using Simetric.Models;

namespace Simetric.DTOs;

public class NotaCreditoListDto
{
    public int Sec { get; set; }
    public string NumeroNotaCredito { get; set; } = "";
    public string Serie { get; set; } = "";
    public string NumeroCompleto => $"{FormatearSerie(Serie)}-{NumeroNotaCredito.PadLeft(9, '0')}";
    public string TipoIdentificacionCliente { get; set; } = "";

    public string Cliente { get; set; } = "";
    public string IdentificacionCliente { get; set; } = "";
    public string NumeroDocModificado { get; set; } = "";
    public DateTime? FechaDocumentoModificado { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string Motivo { get; set; } = "";
    public bool Estado { get; set; }
    public string Autorizado { get; set; } = "";
    public string NumeroAutorizacion { get; set; } = "";
    public string MensajeSri { get; set; } = "";
    public DateTime? FechaAutorizacion { get; set; }
    public string ClaveAcceso { get; set; } = "";
    public string XmlUrl { get; set; } = "";
    public DateTime? FechaVencimientoDocumento { get; set; }
    public decimal SaldoPendienteDocumento { get; set; }

    private static string FormatearSerie(string? serie)
    {
        var limpio = (serie ?? "").Replace("-", "").Trim();
        if (limpio.Length == 6)
            return $"{limpio[..3]}-{limpio.Substring(3, 3)}";

        return serie ?? "";
    }
}

public class NotaCreditoDetalleViewDto
{
    public NotaCredito NotaCredito { get; set; } = new();
    public Cliente? Cliente { get; set; }
    public Emisor? Emisor { get; set; }

    public string TipoIdentificacionCliente { get; set; } = "";

    public string NumeroCompleto { get; set; } = "";
    public string NumeroDocModificadoVisual { get; set; } = "";
    public string XmlUrl { get; set; } = "";

    public List<NotaCreditoDetalleLineaDto> Detalles { get; set; } = new();
}

public class NotaCreditoDetalleLineaDto
{
    public string CodigoInterno { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TarifaIva { get; set; }
    public decimal ValorIva { get; set; }
    public decimal Total { get; set; }
}
