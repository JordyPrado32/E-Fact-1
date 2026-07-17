using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("DETALLEFACTURA")]
public partial class Detallefactura
{
    [Key]
    [Column("CODLINEA")]
    public int Codlinea { get; set; }

    [Column("CODFACTURA")]
    public int Codfactura { get; set; }

    [Column("CODPRODUCTO")]
    public int Codproducto { get; set; }

    [Column("CODPRINCIPAL")]
    public string? Codprincipal { get; set; }

    [Column("CODAUXILIAR")]
    public string? Codauxiliar { get; set; }

    [Column("CANTPRODUCTO")]
    public decimal Cantproducto { get; set; }

    [Column("DESCRIPPRODUCTO")]
    public string? Descripproducto { get; set; }

    [Column("PRECIOPRODUCTO")]
    public decimal Precioproducto { get; set; }

    [Column("DESCUENTO")]
    public decimal? Descuento { get; set; }

    [Column("VALORTPRODUCTO")]
    public decimal Valortproducto { get; set; }

    [Column("VALORIVA")]
    public decimal Valoriva { get; set; }

    [Column("VALORTOTAL")]
    public decimal Valortotal { get; set; }

    [Column("TARIFA")]
    public int Tarifa { get; set; }

    [Column("VALORICE")]
    public decimal? Valorice { get; set; }

    [Column("COSTO")]
    public decimal? Costo { get; set; }

    [Column("BONIFICACION")]
    public int? Bonificacion { get; set; }


    // --- REPARACIÓN DEFINITIVA ---
    [NotMapped]
    public int? ProductoCodigo { get; set; }
    // Esta propiedad NO debe tener un objeto de navegación a Producto
    // si no tienes configurado el Fluent API correctamente.
    // Al NO poner public Producto Producto {get;set;}, evitamos que EF invente 'ProductoCodigo'

    [ForeignKey(nameof(Codfactura))]
    public virtual Factura? Factura { get; set; }
}