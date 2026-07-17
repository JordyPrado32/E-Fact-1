using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ClienteDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [JsonPropertyName("identificacion")]
    public string? Identificacion { get; set; }

    [JsonPropertyName("numeroNotificacion")]
    public string? NumeroNotificacion { get; set; }

    [JsonPropertyName("correo")]
    public string? Correo { get; set; }

    [JsonPropertyName("direccion")]
    public string? Direccion { get; set; }

    [JsonPropertyName("tipoIdentificacion")]
    public string? TipoIdentificacion { get; set; }
}
