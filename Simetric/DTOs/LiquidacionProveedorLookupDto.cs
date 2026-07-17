namespace Simetric.DTOs;

public class LiquidacionProveedorLookupDto
{
    public int? CodCliente { get; set; }
    public string Identificacion { get; set; } = "";
    public string TipoIdentificacion { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string TelefonoFijo { get; set; } = "";
    public string TelefonoMovil { get; set; } = "";
    public string Correo { get; set; } = "";
}
