using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_ORDEN_COMPRA", Schema = "dbo")]
public sealed class EContaxOrdenCompra
{
    [Key]
    [Column("id_orden_compra")]
    public int IdOrdenCompra { get; set; }

    [Column("id_empresa")]
    public int IdEmpresa { get; set; }

    [Column("id_sucursal")]
    public int IdSucursal { get; set; }

    [Column("id_proveedor_cliente")]
    public int IdProveedorCliente { get; set; }

    [Column("numero_orden")]
    public string NumeroOrden { get; set; } = string.Empty;

    [Column("ruc_proveedor")]
    public string RucProveedor { get; set; } = string.Empty;

    [Column("nombre_proveedor")]
    public string NombreProveedor { get; set; } = string.Empty;

    [Column("fecha_emision")]
    public DateTime FechaEmision { get; set; }

    [Column("fecha_entrega_estimada")]
    public DateTime? FechaEntregaEstimada { get; set; }

    [Column("forma_pago")]
    public string? FormaPago { get; set; }

    [Column("plazo_pago")]
    public int? PlazoPago { get; set; }

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "BORRADOR";

    [Column("subtotal", TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column("iva", TypeName = "decimal(18,2)")]
    public decimal Iva { get; set; }

    [Column("total", TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [Column("usuario_creacion")]
    public int UsuarioCreacion { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; }

    [Column("usuario_actualizacion")]
    public int? UsuarioActualizacion { get; set; }

    [Column("fecha_actualizacion")]
    public DateTime? FechaActualizacion { get; set; }

    public ICollection<EContaxOrdenCompraDetalle> Detalles { get; set; } = new List<EContaxOrdenCompraDetalle>();
    public ICollection<EContaxOrdenCompraEstadoHistorial> Historial { get; set; } = new List<EContaxOrdenCompraEstadoHistorial>();
}

[Table("ECONTAX_ORDEN_COMPRA_DETALLE", Schema = "dbo")]
public sealed class EContaxOrdenCompraDetalle
{
    [Key]
    [Column("id_detalle")]
    public int IdDetalle { get; set; }

    [Column("id_orden_compra")]
    public int IdOrdenCompra { get; set; }

    [Column("codigo_producto")]
    public int CodigoProducto { get; set; }

    [Column("codigo_principal")]
    public string? CodigoPrincipal { get; set; }

    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Column("cantidad", TypeName = "decimal(18,4)")]
    public decimal Cantidad { get; set; }

    [Column("precio_unitario", TypeName = "decimal(18,4)")]
    public decimal PrecioUnitario { get; set; }

    [Column("descuento", TypeName = "decimal(18,2)")]
    public decimal Descuento { get; set; }

    [Column("tarifa_iva")]
    public int TarifaIva { get; set; }

    [Column("subtotal", TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column("iva", TypeName = "decimal(18,2)")]
    public decimal Iva { get; set; }

    [Column("total", TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [ForeignKey(nameof(IdOrdenCompra))]
    public EContaxOrdenCompra? OrdenCompra { get; set; }
}

[Table("ECONTAX_ORDEN_COMPRA_HISTORIAL", Schema = "dbo")]
public sealed class EContaxOrdenCompraEstadoHistorial
{
    [Key]
    [Column("id_historial")]
    public int IdHistorial { get; set; }

    [Column("id_orden_compra")]
    public int IdOrdenCompra { get; set; }

    [Column("estado_anterior")]
    public string? EstadoAnterior { get; set; }

    [Column("estado_nuevo")]
    public string EstadoNuevo { get; set; } = string.Empty;

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("usuario")]
    public int Usuario { get; set; }

    [Column("fecha")]
    public DateTime Fecha { get; set; }

    [ForeignKey(nameof(IdOrdenCompra))]
    public EContaxOrdenCompra? OrdenCompra { get; set; }
}

[Table("ECONTAX_RECEPCION_COMPRA", Schema = "dbo")]
public sealed class EContaxRecepcionCompra
{
    [Key]
    [Column("id_recepcion")]
    public int IdRecepcion { get; set; }

    [Column("id_empresa")]
    public int IdEmpresa { get; set; }

    [Column("id_sucursal")]
    public int IdSucursal { get; set; }

    [Column("id_orden_compra")]
    public int IdOrdenCompra { get; set; }

    [Column("numero_recepcion")]
    public string NumeroRecepcion { get; set; } = string.Empty;

    [Column("fecha_recepcion")]
    public DateTime FechaRecepcion { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "BORRADOR";

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("usuario_creacion")]
    public int UsuarioCreacion { get; set; }

    [Column("fecha_creacion")]
    public DateTime FechaCreacion { get; set; }

    [Column("usuario_actualizacion")]
    public int? UsuarioActualizacion { get; set; }

    [Column("fecha_actualizacion")]
    public DateTime? FechaActualizacion { get; set; }

    public ICollection<EContaxRecepcionCompraDetalle> Detalles { get; set; } = new List<EContaxRecepcionCompraDetalle>();
    public ICollection<EContaxRecepcionCompraHistorial> Historial { get; set; } = new List<EContaxRecepcionCompraHistorial>();
}

[Table("ECONTAX_RECEPCION_COMPRA_DETALLE", Schema = "dbo")]
public sealed class EContaxRecepcionCompraDetalle
{
    [Key]
    [Column("id_detalle_recepcion")]
    public int IdDetalleRecepcion { get; set; }

    [Column("id_recepcion")]
    public int IdRecepcion { get; set; }

    [Column("id_detalle_orden_compra")]
    public int IdDetalleOrdenCompra { get; set; }

    [Column("codigo_producto")]
    public int CodigoProducto { get; set; }

    [Column("codigo_principal")]
    public string? CodigoPrincipal { get; set; }

    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Column("cantidad_ordenada", TypeName = "decimal(18,4)")]
    public decimal CantidadOrdenada { get; set; }

    [Column("cantidad_recibida", TypeName = "decimal(18,4)")]
    public decimal CantidadRecibida { get; set; }

    [Column("cantidad_diferencia", TypeName = "decimal(18,4)")]
    public decimal CantidadDiferencia { get; set; }

    [Column("observacion")]
    public string? Observacion { get; set; }

    [ForeignKey(nameof(IdRecepcion))]
    public EContaxRecepcionCompra? Recepcion { get; set; }
}

[Table("ECONTAX_RECEPCION_COMPRA_HISTORIAL", Schema = "dbo")]
public sealed class EContaxRecepcionCompraHistorial
{
    [Key]
    [Column("id_historial")]
    public int IdHistorial { get; set; }

    [Column("id_recepcion")]
    public int IdRecepcion { get; set; }

    [Column("estado_anterior")]
    public string? EstadoAnterior { get; set; }

    [Column("estado_nuevo")]
    public string EstadoNuevo { get; set; } = string.Empty;

    [Column("observacion")]
    public string? Observacion { get; set; }

    [Column("usuario")]
    public int Usuario { get; set; }

    [Column("fecha")]
    public DateTime Fecha { get; set; }

    [ForeignKey(nameof(IdRecepcion))]
    public EContaxRecepcionCompra? Recepcion { get; set; }
}
