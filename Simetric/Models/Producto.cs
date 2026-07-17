using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

public partial class Producto
{
    [Key]
    public int Codigo { get; set; }

    public string? CodigoPrincipal { get; set; }

    // ya no lo usarás en pantalla, pero lo dejamos por compatibilidad
    public string? CodAuxiliar { get; set; }

    public string? Nombre { get; set; }

    // Precio 1
    public decimal? ValorUnitario { get; set; }

    [Column("PRECIO2")]
    public decimal? Precio2 { get; set; }

    [Column("PRECIO3")]
    public decimal? Precio3 { get; set; }

    // ahora será Categoría
    public int? TipoProducto { get; set; }

    [Column("CODIGOIMPUESTO")]
    public string? Codigoimpuesto { get; set; }

    public string? Porcentajeimpuesto { get; set; }

    [Column("IDUSUARIO")]
    public int? Idusuario { get; set; }

    public int? Idempresa { get; set; }

    public int? Idsucursal { get; set; }

    public bool? Estado { get; set; }

    // se elimina de pantalla, pero lo dejamos por compatibilidad
    public decimal? Margen { get; set; }

    public string? Codigocontable { get; set; }

    // se elimina de pantalla, pero lo dejamos por compatibilidad
    public bool? Facturable { get; set; }

    [NotMapped]
    public int? Idclasificacion { get; set; }

    public int? Idproveedor { get; set; }

    public int? Idmarca { get; set; }

    [NotMapped]
    public int? Idsubclasificacion { get; set; }

    public bool? Perecible { get; set; }

    public string? Observacion { get; set; }

    // aquí guardaremos PRODUCTO o SERVICIO
    public string? Tipocompravena { get; set; }

    public bool? Inventario { get; set; }

    [Column("IDSUBTIPO")]
    public int? Idsubtipo { get; set; }

    public virtual Codigosimpuesto? CodigoimpuestoNavigation { get; set; }

    public virtual ICollection<Detallefactura> Detallefacturas { get; set; } = new List<Detallefactura>();

    public virtual Porcentajeiva? PorcentajeimpuestoNavigation { get; set; }

    public virtual Productotipo? TipoProductoNavigation { get; set; }

    [ForeignKey("Idsubtipo")]
    public virtual Productosubtipo? IdsubtipoNavigation { get; set; }
}