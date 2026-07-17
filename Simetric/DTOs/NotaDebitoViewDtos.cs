using Simetric.Models;

namespace Simetric.DTOs;

public class NotaDebitoListDto
{
    public int Sec { get; set; }
    public string NumeroNotaDebito { get; set; } = string.Empty;
    public string Serie { get; set; } = string.Empty;
    public string NumeroCompleto => FormatearNumeroCompleto(Serie, NumeroNotaDebito);
    public string TipoIdentificacionCliente { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string IdentificacionCliente { get; set; } = string.Empty;
    public string NumeroDocModificado { get; set; } = string.Empty;
    public string NumeroDocModificadoVisual { get; set; } = string.Empty;
    public DateTime? FechaDocumentoModificado { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public bool Estado { get; set; }
    public string Autorizado { get; set; } = string.Empty;
    public string NumeroAutorizacion { get; set; } = string.Empty;
    public string MensajeSri { get; set; } = string.Empty;
    public DateTime? FechaAutorizacion { get; set; }
    public string XmlUrl { get; set; } = string.Empty;

    private static string FormatearNumeroCompleto(string? serie, string? secuencial)
    {
        var s = (serie ?? string.Empty).Replace("-", string.Empty).Trim();
        var n = (secuencial ?? string.Empty).Trim().PadLeft(9, '0');

        if (s.Length == 6)
            return $"{s[..3]}-{s.Substring(3, 3)}-{n}";

        return n;
    }
}

public class NotaDebitoDetalleViewDto
{
    public NotaDebito NotaDebito { get; set; } = new();
    public Cliente? Cliente { get; set; }
    public Emisor? Emisor { get; set; }
    public string TipoIdentificacionCliente { get; set; } = string.Empty;
    public string NumeroCompleto { get; set; } = string.Empty;
    public string NumeroDocModificadoVisual { get; set; } = string.Empty;
    public string XmlUrl { get; set; } = string.Empty;
    public string FormaPago { get; set; } = string.Empty;
    public string FormaPagoNombre { get; set; } = string.Empty;
    public int? DiasPlazo { get; set; }
    public List<NotaDebitoDetalleLineaDto> Detalles { get; set; } = new();
}

public class NotaDebitoDetalleLineaDto
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TarifaIva { get; set; }
    public decimal ValorIce { get; set; }
    public decimal ValorIva { get; set; }
    public decimal Total { get; set; }
}
