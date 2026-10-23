using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_PRODUCTO_PROVEEDOR", Schema = "dbo")]
public sealed class EContaxProductoProveedor
{
    [Key]
    [Column("id_producto_proveedor")]
    public int IdProductoProveedor { get; set; }

    [Column("id_empresa")]
    public int IdEmpresa { get; set; }

    [Column("codigo_producto")]
    public int CodigoProducto { get; set; }

    [Column("id_proveedor_cliente")]
    public int IdProveedorCliente { get; set; }

    [Column("codigo_producto_proveedor")]
    public string? CodigoProductoProveedor { get; set; }

    [Column("precio_compra", TypeName = "decimal(18,4)")]
    public decimal PrecioCompra { get; set; }

    [Column("es_principal")]
    public bool EsPrincipal { get; set; }

    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("fecha_actualizacion")]
    public DateTime FechaActualizacion { get; set; }
}
