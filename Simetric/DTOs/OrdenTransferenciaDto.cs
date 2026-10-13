namespace Simetric.DTOs;

public sealed class OrdenTransferenciaRequest
{
    public string? IdempotencyKey { get; set; }
    public string Banco { get; set; } = string.Empty;
    public string TitularCuenta { get; set; } = string.Empty;
    public string CuentaOrigen { get; set; } = string.Empty;
    public string NumeroComprobante { get; set; } = string.Empty;
    public string? ComprobanteBase64 { get; set; }
    public ClienteOrdenTransferenciaDto Cliente { get; set; } = new();
    public List<ProductoOrdenTransferenciaDto> Productos { get; set; } = new();
}

public sealed class ClienteOrdenTransferenciaDto
{
    public int? IdUsuario { get; set; }
    public string? Email { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
}

public sealed class ProductoOrdenTransferenciaDto
{
    public string Tipo { get; set; } = string.Empty;
    public int CantidadDocumentos { get; set; }
    public string? VigenciaFirma { get; set; }
}
