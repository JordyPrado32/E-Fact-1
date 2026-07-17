using System.ComponentModel.DataAnnotations;

namespace Simetric.DTOs.EContax;

public sealed class EContaxProductoDto
{
    public int Codigo { get; set; }
    public string? Nombre { get; set; }
    public string? CodigoPrincipal { get; set; }
    public decimal? ValorUnitario { get; set; }
    public decimal? Precio2 { get; set; }
    public decimal? Precio3 { get; set; }
    public string? TipoCompravena { get; set; }
    public int? TipoProducto { get; set; }
    public int? Idsubtipo { get; set; }
    public string? Codigoimpuesto { get; set; }
    public string? Porcentajeimpuesto { get; set; }
    public bool? Estado { get; set; }
    public string? Observacion { get; set; }
    public int? Idusuario { get; set; }
    public int? Idempresa { get; set; }
    public int? Idsucursal { get; set; }
    public string? NombreSucursal { get; set; }
}

public sealed class EContaxProductoUpsertDto
{
    public int Codigo { get; set; }
    public string? Nombre { get; set; }
    public string? CodigoPrincipal { get; set; }
    public decimal? ValorUnitario { get; set; }
    public decimal? Precio2 { get; set; }
    public decimal? Precio3 { get; set; }
    public string? TipoCompravena { get; set; }
    public int? TipoProducto { get; set; }
    public int? Idsubtipo { get; set; }
    public string? Codigoimpuesto { get; set; }
    public string? Porcentajeimpuesto { get; set; }
    public bool? Estado { get; set; }
    public string? Observacion { get; set; }
    public int? Idsucursal { get; set; }
}
