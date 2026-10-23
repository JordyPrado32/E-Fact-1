namespace Simetric.DTOs;

public class LiquidacionProveedorLookupDto
{
    public int? CodCliente { get; set; }
    public int? IdProveedor { get; set; }
    public string Identificacion { get; set; } = "";
    public string TipoIdentificacion { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string TelefonoFijo { get; set; } = "";
    public string TelefonoMovil { get; set; } = "";
    public string Correo { get; set; } = "";
    public string FormaPago { get; set; } = "";
    public int? PlazoPago { get; set; }
    public string CuentaContable { get; set; } = "";
    public bool LlevaRetencion { get; set; }
    public string TipoCuenta { get; set; } = "";
    public string NumeroCuenta { get; set; } = "";
    public int? InstitucionFin { get; set; }
}
