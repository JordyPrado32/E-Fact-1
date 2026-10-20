namespace Simetric.DTOs.EContax;

public class EContaxOrdenCompraListaDto
{
    public int IdOrdenCompra { get; set; }
    public string NumeroOrden { get; set; } = string.Empty;
    public int IdSucursal { get; set; }
    public int IdProveedor { get; set; }
    public string NombreProveedor { get; set; } = string.Empty;
    public string RucProveedor { get; set; } = string.Empty;
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int Lineas { get; set; }
}

public sealed class EContaxOrdenCompraDto : EContaxOrdenCompraListaDto
{
    public string? FormaPago { get; set; }
    public int? PlazoPago { get; set; }
    public string? Observacion { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public List<EContaxOrdenCompraDetalleDto> Detalles { get; set; } = new();
}

public sealed class EContaxOrdenCompraDetalleDto
{
    public int IdDetalle { get; set; }
    public int CodigoProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
    public int TarifaIva { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
}

public sealed class EContaxOrdenCompraUpsertDto
{
    public int? IdSucursal { get; set; }
    public int IdProveedor { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    public string? FormaPago { get; set; }
    public int? PlazoPago { get; set; }
    public string? Observacion { get; set; }
    public List<EContaxOrdenCompraDetalleUpsertDto> Detalles { get; set; } = new();
}

public sealed class EContaxOrdenCompraDetalleUpsertDto
{
    public int CodigoProducto { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }
}

public sealed class EContaxProveedorLookupDto
{
    public int IdProveedor { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Identificacion { get; set; } = string.Empty;
}

public sealed class EContaxOrdenCompraEstadoDto
{
    public string Estado { get; set; } = string.Empty;
    public string? Observacion { get; set; }
}

public class EContaxRecepcionCompraListaDto
{
    public int IdRecepcion { get; set; }
    public string NumeroRecepcion { get; set; } = string.Empty;
    public int IdOrdenCompra { get; set; }
    public string NumeroOrden { get; set; } = string.Empty;
    public string NombreProveedor { get; set; } = string.Empty;
    public DateTime FechaRecepcion { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal TotalRecibido { get; set; }
    public int Lineas { get; set; }
}

public sealed class EContaxRecepcionCompraDto : EContaxRecepcionCompraListaDto
{
    public int IdSucursal { get; set; }
    public string? Observacion { get; set; }
    public List<EContaxRecepcionCompraDetalleDto> Detalles { get; set; } = new();
}

public sealed class EContaxRecepcionCompraDetalleDto
{
    public int IdDetalleRecepcion { get; set; }
    public int IdDetalleOrdenCompra { get; set; }
    public int CodigoProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal CantidadOrdenada { get; set; }
    public decimal CantidadYaRecibida { get; set; }
    public decimal CantidadRecibida { get; set; }
    public decimal CantidadPendiente { get; set; }
    public decimal CantidadDiferencia { get; set; }
    public string? Observacion { get; set; }
}

public sealed class EContaxRecepcionOrdenDto
{
    public int IdOrdenCompra { get; set; }
    public string NumeroOrden { get; set; } = string.Empty;
    public string NombreProveedor { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public List<EContaxRecepcionOrdenDetalleDto> Detalles { get; set; } = new();
}

public sealed class EContaxRecepcionOrdenDetalleDto
{
    public int IdDetalleOrdenCompra { get; set; }
    public int CodigoProducto { get; set; }
    public string? CodigoPrincipal { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal CantidadOrdenada { get; set; }
    public decimal CantidadYaRecibida { get; set; }
    public decimal CantidadPendiente { get; set; }
}

public sealed class EContaxRecepcionCompraUpsertDto
{
    public int IdOrdenCompra { get; set; }
    public DateTime? FechaRecepcion { get; set; }
    public string? Observacion { get; set; }
    public List<EContaxRecepcionCompraDetalleUpsertDto> Detalles { get; set; } = new();
}

public sealed class EContaxRecepcionCompraDetalleUpsertDto
{
    public int IdDetalleOrdenCompra { get; set; }
    public decimal CantidadRecibida { get; set; }
    public string? Observacion { get; set; }
}
