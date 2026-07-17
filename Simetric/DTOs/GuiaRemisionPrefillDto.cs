namespace Simetric.DTOs;

public sealed class GuiaRemisionPrefillDto
{
    public string? SerieFactura { get; set; }
    public string? NumeroFactura { get; set; }
    public DateTime? FechaFactura { get; set; }
    public string? ClienteIdentificacion { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ClienteDireccion { get; set; }
    public string? EmisorDireccion { get; set; }
    public string? EmisorCodEstablecimiento { get; set; }
    public List<GuiaRemisionPrefillDetalleDto> Detalles { get; set; } = new();
}

public sealed class GuiaRemisionPrefillDetalleDto
{
    public string? CodigoInterno { get; set; }
    public string? CodigoAdicional { get; set; }
    public string? Descripcion { get; set; }
    public int Cantidad { get; set; }
}
