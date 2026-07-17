using System.ComponentModel.DataAnnotations;

namespace Simetric.DTOs.EContax;

public sealed class EContaxClienteDto
{
    public int Codcliente { get; set; }
    public string? Apellidos { get; set; }
    public string? Nombres { get; set; }
    public string? Nombrecomercial { get; set; }
    public string? Nombrerazonsocial { get; set; }
    public string? Numeroidentificacion { get; set; }
    public string? Direccion { get; set; }
    public string? Telefonoconvencional { get; set; }
    public string? Celular { get; set; }
    public string? Correo { get; set; }
    public List<string> CorreosAdicionales { get; set; } = new();
    public string? Observaciones { get; set; }
    public string? Oblgconta { get; set; }
    public int TipoCliente { get; set; }
    public bool? Estado { get; set; }
    public int? Pais { get; set; }
    public int? Provincia { get; set; }
    public int? Ciudad { get; set; }
    public int? Tipoidentificacion { get; set; }
    public int? Idempresa { get; set; }
}

public sealed class EContaxClienteUpsertDto
{
    public int Codcliente { get; set; }
    public string? Apellidos { get; set; }
    public string? Nombres { get; set; }
    public string? Nombrecomercial { get; set; }
    public string? Nombrerazonsocial { get; set; }
    public string? Numeroidentificacion { get; set; }
    public string? Direccion { get; set; }
    public string? Telefonoconvencional { get; set; }
    public string? Celular { get; set; }
    public string? Correo { get; set; }
    public string? Observaciones { get; set; }
    public int TipoCliente { get; set; }
    public bool? Estado { get; set; } = true;
    public int? Pais { get; set; }
    public int? Provincia { get; set; }
    public int? Ciudad { get; set; }
    public int? Tipoidentificacion { get; set; }
    public List<string> CorreosAdicionales { get; set; } = new();
    public string? Oblgconta { get; set; }
}
