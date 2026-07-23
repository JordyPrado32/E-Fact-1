namespace Simetric.DTOs;

public class FacturaListDto
{
    public int Codfactura { get; set; }
    public string? Numfactura { get; set; }
    public string? Serie { get; set; }
    public DateTime? FechaEmision { get; set; }
    public string? EstadoSri { get; set; }
    public bool? Autorizado { get; set; }
    public string? NumeroAutorizacion { get; set; }
    public string? MensajeSri { get; set; }
    public DateTime? FechaAutorizacion { get; set; }
    public decimal? Total { get; set; }
    public decimal TotalAbonado { get; set; }
    public string? Tipopago { get; set; }
    public string? EstadoPago { get; set; }
    public string? Cliente { get; set; }
    public decimal? DescuentoGlobalPct { get; set; }
    public decimal? DescuentoGlobalValor { get; set; }
    public string? IdentificacionCliente { get; set; }
    public bool? Estado { get; set; }
    public decimal SaldoPendiente => Math.Max((Total ?? 0m) - TotalAbonado, 0m);
    public bool EstaEnCuentasPorCobrar =>
        SaldoPendiente > 0m &&
        (string.Equals(Tipopago?.Trim(), "19", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(EstadoPago?.Trim(), "PENDIENTE", StringComparison.OrdinalIgnoreCase));

    public string NumeroCompleto
    {
        get
        {
            var serieLimpia = (Serie ?? string.Empty).Replace("-", string.Empty).Trim();
            var numeroLimpio = (Numfactura ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(serieLimpia))
                return string.IsNullOrWhiteSpace(numeroLimpio) ? "-" : numeroLimpio;

            if (serieLimpia.Length == 6)
                serieLimpia = $"{serieLimpia[..3]}-{serieLimpia.Substring(3, 3)}";

            if (string.IsNullOrWhiteSpace(numeroLimpio))
                return serieLimpia;

            return $"{serieLimpia}-{numeroLimpio.PadLeft(9, '0')}";
        }
    }
}
