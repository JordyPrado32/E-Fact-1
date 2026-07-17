using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("DETALLESNOTADEBITO", Schema = "dbo")]
public class DetalleNotaDebito
{
    [Key]
    [Column("codLinea")]
    public int CodLinea { get; set; }

    [Required]
    [Column("codNotaDebito")]
    public int CodNotaDebito { get; set; }

    [Required]
    [Column("codProducto")]
    public int CodProducto { get; set; }

    [Column("codPrincipal")]
    public string? CodPrincipal { get; set; }

    [Column("codAuxiliar")]
    public string? CodAuxiliar { get; set; }

    [Column("cantProducto", TypeName = "decimal(18,2)")]
    public decimal? CantProducto { get; set; }

    [Column("descripProducto")]
    public string? DescripProducto { get; set; }

    [Column("precioProducto", TypeName = "decimal(18,2)")]
    public decimal? PrecioProducto { get; set; }

    [Column("descuento", TypeName = "decimal(18,2)")]
    public decimal? Descuento { get; set; }

    [Column("valorTProducto", TypeName = "decimal(18,2)")]
    public decimal? ValorTProducto { get; set; }

    [Column("valorICE", TypeName = "decimal(18,2)")]
    public decimal? ValorIce { get; set; }

    [Column("valorIVA", TypeName = "decimal(18,2)")]
    public decimal? ValorIva { get; set; }

    [Column("BI_IRBPNR", TypeName = "decimal(18,2)")]
    public decimal? BiIrbpnr { get; set; }

    [Column("valorBI_IRBPNR", TypeName = "decimal(18,2)")]
    public decimal? ValorBiIrbpnr { get; set; }

    [Column("porcentajeIVA", TypeName = "decimal(18,2)")]
    public decimal? PorcentajeIva { get; set; }

    [Column("codigoImp")]
    public int? CodigoImp { get; set; }

    [Column("costo", TypeName = "numeric(18,2)")]
    public decimal? Costo { get; set; }
}
