using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class FacturaDraftDto
{
    [JsonPropertyName("cliente")]
    public ClienteDto? Cliente { get; set; }

    [JsonPropertyName("items")]
    public List<FacturaItemDraftDto> Items { get; set; } = new();

    [JsonPropertyName("formaPago")]
    public string? FormaPago { get; set; }

    [JsonPropertyName("diasCredito")]
    public int? DiasCredito { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public DateTime? FechaVencimiento { get; set; }

    [JsonPropertyName("descuentoGlobalPorcentaje")]
    public decimal? DescuentoGlobalPorcentaje { get; set; }

    [JsonPropertyName("descuentoGlobalValor")]
    public decimal? DescuentoGlobalValor { get; set; }

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; }

    [JsonPropertyName("descuento")]
    public decimal Descuento { get; set; }

    [JsonPropertyName("impuesto")]
    public decimal Impuesto { get; set; }

    [JsonPropertyName("ivaDetalles")]
    public List<FacturaTaxBreakdownDto> IvaDetalles { get; set; } = new();

    [JsonPropertyName("total")]
    public decimal Total { get; set; }
}

public sealed class FacturaTaxBreakdownDto
{
    [JsonPropertyName("tarifaPorcentaje")]
    public decimal TarifaPorcentaje { get; set; }

    [JsonPropertyName("baseImponible")]
    public decimal BaseImponible { get; set; }

    [JsonPropertyName("valorIva")]
    public decimal ValorIva { get; set; }
}
