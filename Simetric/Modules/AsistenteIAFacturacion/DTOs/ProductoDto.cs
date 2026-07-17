using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ProductoDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("codigoPrincipal")]
    public string? CodigoPrincipal { get; set; }

    [JsonPropertyName("categoria")]
    public string? Categoria { get; set; }

    [JsonPropertyName("subcategoria")]
    public string? Subcategoria { get; set; }

    [JsonPropertyName("precioUnitario")]
    public decimal PrecioUnitario { get; set; }

    [JsonPropertyName("tarifaPorcentaje")]
    public decimal TarifaPorcentaje { get; set; }

    [JsonPropertyName("tipo")]
    public string? Tipo { get; set; }
}
