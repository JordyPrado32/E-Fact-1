using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ProductoCreateRequestDto
{
    [JsonPropertyName("nombre")]
    public string? Nombre { get; set; }

    [JsonPropertyName("codigoPrincipal")]
    public string? CodigoPrincipal { get; set; }

    [JsonPropertyName("precioUnitario")]
    public decimal? PrecioUnitario { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }

    [JsonPropertyName("tarifaPorcentaje")]
    public decimal? TarifaPorcentaje { get; set; }

    [JsonPropertyName("observacion")]
    public string? Observacion { get; set; }
}
