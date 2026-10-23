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
    public string? UnidadMedida { get; set; }
    public decimal FactorConversionInventario { get; set; } = 1m;
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
    public List<int> Idsucursales { get; set; } = new();
    public decimal StockActual { get; set; }
    public decimal StockMinimo { get; set; }
    public decimal StockMaximo { get; set; }
    public bool TieneAlertaInventario { get; set; }
    public List<EContaxProductoProveedorDto> Proveedores { get; set; } = new();
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
    public string? UnidadMedida { get; set; }
    public decimal FactorConversionInventario { get; set; } = 1m;
    public int? TipoProducto { get; set; }
    public int? Idsubtipo { get; set; }
    public string? Codigoimpuesto { get; set; }
    public string? Porcentajeimpuesto { get; set; }
    public bool? Estado { get; set; }
    public string? Observacion { get; set; }
    public int? Idsucursal { get; set; }
    public List<int> Idsucursales { get; set; } = new();
    public List<EContaxProductoProveedorUpsertDto> Proveedores { get; set; } = new();
}

public sealed class EContaxProductoProveedorDto
{
    public int IdProveedor { get; set; }
    public string NombreProveedor { get; set; } = string.Empty;
    public string Identificacion { get; set; } = string.Empty;
    public string? CodigoProductoProveedor { get; set; }
    public decimal PrecioCompra { get; set; }
    public bool EsPrincipal { get; set; }
    public bool Activo { get; set; }
}

public sealed class EContaxProductoProveedorUpsertDto
{
    public int IdProveedor { get; set; }
    public string? CodigoProductoProveedor { get; set; }
    public decimal PrecioCompra { get; set; }
    public bool EsPrincipal { get; set; }
}
