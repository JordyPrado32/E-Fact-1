using System.Text.Json.Serialization;

namespace Simetric.Modules.AsistenteIAFacturacion.DTOs;

public sealed class ClienteCreateRequestDto
{
    [JsonPropertyName("nombreCompleto")]
    public string? NombreCompleto { get; set; }

    [JsonPropertyName("apellidos")]
    public string? Apellidos { get; set; }

    [JsonPropertyName("nombres")]
    public string? Nombres { get; set; }

    [JsonPropertyName("razonSocial")]
    public string? RazonSocial { get; set; }

    [JsonPropertyName("nombreComercial")]
    public string? NombreComercial { get; set; }

    [JsonPropertyName("identificacion")]
    public string? Identificacion { get; set; }

    [JsonPropertyName("correo")]
    public string? Correo { get; set; }

    [JsonPropertyName("celular")]
    public string? Celular { get; set; }

    [JsonPropertyName("telefono")]
    public string? Telefono { get; set; }

    [JsonPropertyName("direccion")]
    public string? Direccion { get; set; }

    [JsonPropertyName("obligadoContabilidad")]
    public string? ObligadoContabilidad { get; set; }

    [JsonPropertyName("esEmpresa")]
    public bool? EsEmpresa { get; set; }

    [JsonPropertyName("pais")]
    public string? Pais { get; set; }

    [JsonPropertyName("provincia")]
    public string? Provincia { get; set; }

    [JsonPropertyName("ciudad")]
    public string? Ciudad { get; set; }
}
