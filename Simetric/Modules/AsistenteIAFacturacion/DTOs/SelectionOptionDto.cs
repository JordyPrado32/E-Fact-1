using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class SelectionOptionDto
{
    [JsonPropertyName("indice")]
    public int Indice { get; set; }

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("etiqueta")]
    public string Etiqueta { get; set; } = string.Empty;

    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    [JsonPropertyName("cliente")]
    public ClienteDto? Cliente { get; set; }

    [JsonPropertyName("producto")]
    public ProductoDto? Producto { get; set; }
}
