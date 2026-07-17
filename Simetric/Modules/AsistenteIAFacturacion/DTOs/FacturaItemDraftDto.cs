using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class FacturaItemDraftDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("productoId")]
    public int? ProductoId { get; set; }

    [JsonPropertyName("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [JsonPropertyName("codigoPrincipal")]
    public string? CodigoPrincipal { get; set; }

    [JsonPropertyName("cantidad")]
    public decimal Cantidad { get; set; }

    [JsonPropertyName("precioUnitario")]
    public decimal PrecioUnitario { get; set; }

    [JsonPropertyName("descuentoPorcentaje")]
    public decimal? DescuentoPorcentaje { get; set; }

    [JsonPropertyName("descuentoValor")]
    public decimal? DescuentoValor { get; set; }

    [JsonPropertyName("descuentoAplicado")]
    public decimal DescuentoAplicado { get; set; }

    [JsonPropertyName("tarifaPorcentaje")]
    public decimal TarifaPorcentaje { get; set; }

    [JsonPropertyName("esServicioManual")]
    public bool EsServicioManual { get; set; }

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    [JsonPropertyName("impuesto")]
    public decimal Impuesto { get; set; }

    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}
