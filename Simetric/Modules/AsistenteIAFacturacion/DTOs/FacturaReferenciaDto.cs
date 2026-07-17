using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class FacturaReferenciaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("numeroFactura")]
    public string NumeroFactura { get; set; } = string.Empty;

    [JsonPropertyName("serie")]
    public string? Serie { get; set; }

    [JsonPropertyName("clienteNombre")]
    public string ClienteNombre { get; set; } = string.Empty;
}
