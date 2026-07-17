namespace Simetric.DTOs;

public class CompraXmlBusquedaDto
{
    public int CodFactura { get; set; }
    public string ClaveAcceso { get; set; } = "";
    public string RucProveedor { get; set; } = "";
    public string RazonSocialProveedor { get; set; } = "";
    public string Secuencial { get; set; } = "";
    public string Serie { get; set; } = "";
    public DateTime? FechaEmision { get; set; }
    public decimal Total { get; set; }
}