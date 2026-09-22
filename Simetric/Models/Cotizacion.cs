using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("COTIZACION", Schema = "dbo")]
public class Cotizacion
{
    [Key]
    [Column("ID_COTIZACION")]
    public int Id { get; set; }

    [Column("ID_USUARIO")]
    public int IdUsuario { get; set; }

    [Column("FECHA_CREACION")]
    public DateTime FechaCreacion { get; set; }

    [Column("TOTAL_ESTIMADO", TypeName = "decimal(18,2)")]
    public decimal TotalEstimado { get; set; }

    public List<CotizacionDetalle> Detalles { get; set; } = new();
}

[Table("COTIZACION_DETALLE", Schema = "dbo")]
public class CotizacionDetalle
{
    [Key]
    [Column("ID_DETALLE")]
    public int Id { get; set; }

    [Column("ID_COTIZACION")]
    public int IdCotizacion { get; set; }

    [Column("CODIGO_PRODUCTO")]
    public int CodigoProducto { get; set; }

    [Column("NOMBRE_PRODUCTO")]
    public string NombreProducto { get; set; } = string.Empty;

    [Column("PRECIO_UNITARIO", TypeName = "decimal(18,2)")]
    public decimal PrecioUnitario { get; set; }

    [Column("CANTIDAD")]
    public int Cantidad { get; set; }

    [Column("TOTAL_LINEA", TypeName = "decimal(18,2)")]
    public decimal TotalLinea { get; set; }

    [ForeignKey(nameof(IdCotizacion))]
    public Cotizacion? Cotizacion { get; set; }
}
