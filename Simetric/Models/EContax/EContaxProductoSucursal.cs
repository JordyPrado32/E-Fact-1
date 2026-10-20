using System.ComponentModel.DataAnnotations.Schema;
using Simetric.Models;

namespace Simetric.Models.EContax;

public sealed class EContaxProductoSucursal
{
    public int CodigoProducto { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CodigoProducto))]
    public Producto? Producto { get; set; }

    public EContaxSucursal? Sucursal { get; set; }
}
