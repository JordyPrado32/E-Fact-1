using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("COMPRASFACTURA")]
public class ComprasFactura
{
    [Key]
    [Column("codFactura")]
    public int CodFactura { get; set; }

    [Column("codClave")]
    public string? CodClave { get; set; }

    [Column("codClientes")]
    public int? CodClientes { get; set; }

    [Column("codEmisor")]
    public int? CodEmisor { get; set; }

    [Column("codRespuesta")]
    public int? CodRespuesta { get; set; }

    [Column("codComprobante")]
    public int? CodComprobante { get; set; }

    [Column("codTranportista")]
    public int? CodTranportista { get; set; }

    [Column("fchAutorizacion")]
    public DateTime? FchAutorizacion { get; set; }

    [Column("numFactura")]
    public string? NumFactura { get; set; }

    [Column("numAutorización")]
    public string? NumAutorizacion { get; set; }

    [Column("codDocumento")]
    public string? CodDocumento { get; set; }

    [Column("guiaRemision")]
    public string? GuiaRemision { get; set; }

    [Column("numRetencion")]
    public string? NumRetencion { get; set; }

    [Column("subtotal12")]
    public decimal? Subtotal12 { get; set; }

    [Column("subtotal0")]
    public decimal? Subtotal0 { get; set; }

    [Column("subtotal")]
    public decimal? Subtotal { get; set; }

    [Column("descuentos")]
    public decimal? Descuentos { get; set; }

    [Column("iva")]
    public decimal? Iva { get; set; }

    [Column("valorTotal")]
    public decimal? ValorTotal { get; set; }

    [Column("fechaVence")]
    public DateTime? FechaVence { get; set; }

    [Column("noImp")]
    public decimal? NoImp { get; set; }

    [Column("exIva")]
    public decimal? ExIva { get; set; }

    [Column("valorICE")]
    public decimal? ValorICE { get; set; }

    [Column("BI_IRBPNR")]
    public decimal? BI_IRBPNR { get; set; }

    [Column("valorBI_IRBPNR")]
    public decimal? ValorBI_IRBPNR { get; set; }

    [Column("usuario")]
    public int? Usuario { get; set; }

    [Column("autorizado")]
    public string? Autorizado { get; set; }

    [Column("mensaje")]
    public string? Mensaje { get; set; }

    [Column("idEmpresa")]
    public int? IdEmpresa { get; set; }

    [Column("idSucursal")]
    public int? IdSucursal { get; set; }

    [Column("serie")]
    public string? Serie { get; set; }

    [Column("fechaAutoSRI")]
    public string? FechaAutoSRI { get; set; }

    [Column("tipoPago")]
    public string? TipoPago { get; set; }

    [Column("DsctAdicionalCod2")]
    public decimal? DsctAdicionalCod2 { get; set; }

    [Column("estadoEnvioSRI")]
    public string? EstadoEnvioSRI { get; set; }

    [Column("subCeroTotal")]
    public decimal? SubCeroTotal { get; set; }

    [Column("subDoceTotal")]
    public decimal? SubDoceTotal { get; set; }

    [Column("subNoImpTotal")]
    public decimal? SubNoImpTotal { get; set; }

    [Column("subExIvaTotal")]
    public decimal? SubExIvaTotal { get; set; }

    [Column("ambiente")]
    public int? Ambiente { get; set; }

    [Column("estado")]
    public bool? Estado { get; set; }

    [Column("tiempoCredito")]
    public int? TiempoCredito { get; set; }

    [Column("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    [Column("idVendedor")]
    public int? IdVendedor { get; set; }

    [Column("diasEntrega")]
    public int? DiasEntrega { get; set; }

    [Column("idAseguradora")]
    public int? IdAseguradora { get; set; }

    [Column("comision")]
    public decimal? Comision { get; set; }

    [Column("idEmpresaPagar")]
    public int? IdEmpresaPagar { get; set; }

    [Column("valorApagar")]
    public decimal? ValorApagar { get; set; }

    [Column("ciudad")]
    public int? Ciudad { get; set; }

    [Column("fechaEntrega")]
    public DateTime? FechaEntrega { get; set; }

    [Column("estadoPago")]
    public string? EstadoPago { get; set; }

    [Column("fechaCancelado")]
    public DateOnly? FechaCancelado { get; set; }

    [Column("edad")]
    public string? Edad { get; set; }

    [Column("estadoAtendido")]
    public string? EstadoAtendido { get; set; }

    [Column("fechaCaducidad")]
    public DateTime? FechaCaducidad { get; set; }

    [Column("txtNumCuotas")]
    public int? TxtNumCuotas { get; set; }

    [Column("tieneRetencion")]
    public bool? TieneRetencion { get; set; }

    [Column("desglosaIva")]
    public bool? DesglosaIva { get; set; }

    [Column("tieneSustentoTributario")]
    public bool? TieneSustentoTributario { get; set; }

    [Column("fechaRegistro")]
    public DateTime? FechaRegistro { get; set; }

    [Column("inventario")]
    public bool? Inventario { get; set; }

    [Column("cuentaContable")]
    public string? CuentaContable { get; set; }

    [Column("contabilizado")]
    public bool? Contabilizado { get; set; }

    [Column("subtotal8")]
    public decimal? Subtotal8 { get; set; }

    [Column("subtotal5")]
    public decimal? Subtotal5 { get; set; }

    [Column("iva5")]
    public decimal? Iva5 { get; set; }

    [Column("iva8")]
    public decimal? Iva8 { get; set; }

    public virtual ICollection<ComprasDetalleFac> Detalles { get; set; } = new List<ComprasDetalleFac>();
}