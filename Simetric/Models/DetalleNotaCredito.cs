using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("DETALLENOTACREDITO", Schema = "dbo")]
public class DetalleNotaCredito
{
    [Key]
    [Column("codLinea")]
    public int CodLinea { get; set; }

    [Required]
    [Column("codNotaCredito")]
    public int CodNotaCredito { get; set; }

    [Required]
    [Column("codProducto")]
    public int CodProducto { get; set; }

    [StringLength(70)]
    [Column("codPrincipal")]
    public string? CodPrincipal { get; set; }

    [StringLength(70)]
    [Column("codAuxiliar")]
    public string? CodAuxiliar { get; set; }

    [Column("cantProducto", TypeName = "decimal(18,2)")]
    public decimal? CantProducto { get; set; }

    [StringLength(1000)]
    [Column("descripProducto")]
    public string? DescripProducto { get; set; }

    [Column("precioProducto", TypeName = "decimal(18,2)")]
    public decimal? PrecioProducto { get; set; }

    [Column("descuento", TypeName = "decimal(18,2)")]
    public decimal? Descuento { get; set; }

    [Column("valorTProducto", TypeName = "decimal(18,2)")]
    public decimal? ValorTProducto { get; set; }

    [Column("valorICE", TypeName = "decimal(18,2)")]
    public decimal? ValorICE { get; set; }

    [Column("valorIVA", TypeName = "decimal(18,2)")]
    public decimal? ValorIVA { get; set; }

    [Column("BI_IRBPNR", TypeName = "decimal(18,2)")]
    public decimal? BiIrbpnr { get; set; }

    [Column("valorBI_IRBPNR", TypeName = "decimal(18,2)")]
    public decimal? ValorBiIrbpnr { get; set; }

    [Column("codImp")]
    public int? CodImp { get; set; }

    [Column("porImp")]
    public int? PorImp { get; set; }

    [Column("tarifa")]
    public int? Tarifa { get; set; }

    [Column("costo", TypeName = "numeric(18,2)")]
    public decimal? Costo { get; set; }
}