using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ClienteCarteraResumenDto
{
    [JsonPropertyName("clienteId")]
    public int ClienteId { get; set; }

    [JsonPropertyName("nombreCliente")]
    public string NombreCliente { get; set; } = string.Empty;

    [JsonPropertyName("identificacion")]
    public string Identificacion { get; set; } = string.Empty;

    [JsonPropertyName("facturasPendientes")]
    public int FacturasPendientes { get; set; }

    [JsonPropertyName("saldoPendiente")]
    public decimal SaldoPendiente { get; set; }
}
