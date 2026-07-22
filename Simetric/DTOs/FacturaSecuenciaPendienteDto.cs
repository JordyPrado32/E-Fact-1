namespace Simetric.DTOs;

public sealed class FacturaSecuenciaPendienteDto
{
    public int Codfactura { get; init; }
    public string Serie { get; init; } = string.Empty;
    public string Secuencial { get; init; } = string.Empty;
    public string EstadoSri { get; init; } = string.Empty;
    public string MensajeSri { get; init; } = string.Empty;
    public string RutaCorreccion { get; init; } = string.Empty;
    public string EtiquetaCorreccion { get; init; } = string.Empty;

    public string NumeroCompleto
    {
        get
        {
            var serie = Serie.Replace("-", string.Empty).Trim();
            var serieVisual = serie.Length == 6
                ? $"{serie[..3]}-{serie[3..]}"
                : serie;
            return $"{serieVisual}-{Secuencial.PadLeft(9, '0')}";
        }
    }
}
