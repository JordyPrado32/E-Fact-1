namespace Simetric.DTOs;

public class CompraDocumentosHistorialItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Fecha { get; set; } = DateTime.Now;
    public int Documentos { get; set; }
    public decimal MontoTotal { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public string Descripcion { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? CustomValue { get; set; }
    public string? EmailDestino { get; set; }
    public bool SaldoAplicado { get; set; }
    public bool EsIlimitado { get; set; }
    public bool EsPermanente { get; set; }
    public DateTime? VigenciaHasta { get; set; }
    public int? CodFactura { get; set; }
    public string? NumeroFactura { get; set; }
    public DateTime? FechaFactura { get; set; }
    public bool? FacturaAutorizada { get; set; }
    public string? EstadoFactura { get; set; }
    public string? MensajeFactura { get; set; }
}
