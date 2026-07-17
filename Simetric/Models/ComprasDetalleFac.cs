using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("COMPRASDETALLEFAC")]
public class ComprasDetalleFac
{
    [Key]
    [Column("codLinea")]
    public int CodLinea { get; set; }

    [Column("codFactura")]
    public int CodFactura { get; set; }

    [Column("codProducto")]
    public int CodProducto { get; set; }

    [Column("codPrincipal")]
    public string? CodPrincipal { get; set; }

    [Column("codAuxiliar")]
    public string? CodAuxiliar { get; set; }

    [Column("cantProducto")]
    public decimal? CantProducto { get; set; }

    [Column("descripProducto")]
    public string? DescripProducto { get; set; }

    [Column("precioProducto")]
    public decimal? PrecioProducto { get; set; }

    [Column("descuento")]
    public decimal? Descuento { get; set; }

    [Column("valorTProducto")]
    public decimal? ValorTProducto { get; set; }

    [Column("valorICE")]
    public decimal? ValorICE { get; set; }

    [Column("valorIVA")]
    public decimal? ValorIVA { get; set; }

    [Column("BI_IRBPNR")]
    public decimal? BI_IRBPNR { get; set; }

    [Column("valorBI_IRBPNR")]
    public decimal? ValorBI_IRBPNR { get; set; }

    [Column("valorTotal")]
    public decimal? ValorTotal { get; set; }

    [Column("codImp")]
    public int? CodImp { get; set; }

    [Column("porImp")]
    public int? PorImp { get; set; }

    [Column("tarifa")]
    public int? Tarifa { get; set; }

    [Column("costo")]
    public decimal? Costo { get; set; }

    [Column("estadoAtendido")]
    public string? EstadoAtendido { get; set; }

    [Column("comision")]
    public decimal? Comision { get; set; }

    [Column("idvendedor")]
    public int? IdVendedor { get; set; }

    [Column("valorEmpPagar")]
    public decimal? ValorEmpPagar { get; set; }

    [Column("idEmpPagar")]
    public int? IdEmpPagar { get; set; }

    [Column("idAseguradora")]
    public int? IdAseguradora { get; set; }

    [Column("estadoPagoComision")]
    public string? EstadoPagoComision { get; set; }

    [Column("estadoPagoEmpresa")]
    public string? EstadoPagoEmpresa { get; set; }

    [Column("fechaPagoComision")]
    public DateOnly? FechaPagoComision { get; set; }

    [Column("fechaPagoEmpresa")]
    public DateOnly? FechaPagoEmpresa { get; set; }

    [Column("lote")]
    public string? Lote { get; set; }

    [Column("fechaCaduca")]
    public DateOnly? FechaCaduca { get; set; }

    [Column("cuentaContable")]
    public string? CuentaContable { get; set; }

    [Column("cantidadRecibida")]
    public int? CantidadRecibida { get; set; }

    [Column("inventariado")]
    public bool? Inventariado { get; set; }

    [Column("temperatura")]
    public string? Temperatura { get; set; }

    [Column("observacion")]
    public string? Observacion { get; set; }

    [ForeignKey(nameof(CodFactura))]
    public virtual ComprasFactura? CompraFactura { get; set; }
}