using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ChatFacturaRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("mensaje")]
    public string Mensaje { get; set; } = string.Empty;

    [JsonPropertyName("modo")]
    public string Modo { get; set; } = "texto";

    [JsonPropertyName("contexto")]
    public string? Contexto { get; set; }
}
