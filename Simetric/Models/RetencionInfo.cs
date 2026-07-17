using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("RETENCIONINFO")]
public class RetencionInfo
{
    [Key]
    [Column("sec")]
    public int Sec { get; set; }

    [Column("numRetencion")]
    public string? NumRetencion { get; set; }

    [Column("fecha")]
    public DateTime? Fecha { get; set; }

    [Column("periodoFiscal")]
    public string? PeriodoFiscal { get; set; }

    [Column("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    [Column("tipoIdentificacion")]
    public string? TipoIdentificacion { get; set; }

    [Column("idCliente")]
    public string? IdCliente { get; set; }

    [Column("clave")]
    public string? Clave { get; set; }

    [Column("numAutorizacion")]
    public string? NumAutorizacion { get; set; }

    [Column("fechaAutorizaSri")]
    public string? FechaAutorizaSri { get; set; }

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

    [Column("ambiente")]
    public int? Ambiente { get; set; }

    [Column("icCompra")]
    public int? IcCompra { get; set; }

    [Column("idRetIva")]
    public string? IdRetIva { get; set; }

    [Column("descripcionRetIva")]
    public string? DescripcionRetIva { get; set; }

    [Column("baseRetIva")]
    public decimal? BaseRetIva { get; set; }

    [Column("valorRetIva")]
    public decimal? ValorRetIva { get; set; }

    [Column("tipoRetIva")]
    public string? TipoRetIva { get; set; }

    [Column("idRetRenta")]
    public string? IdRetRenta { get; set; }

    [Column("descripcionRetRenta")]
    public string? DescripcionRetRenta { get; set; }

    [Column("baseRetRenta")]
    public decimal? BaseRetRenta { get; set; }

    [Column("valorRetRenta")]
    public decimal? ValorRetRenta { get; set; }

    [Column("tipoRetRenta")]
    public string? TipoRetRenta { get; set; }

    [Column("estado")]
    public string? Estado { get; set; }

    [Column("idRetIva1")]
    public string? IdRetIva1 { get; set; }

    [Column("descripcionRetIva1")]
    public string? DescripcionRetIva1 { get; set; }

    [Column("baseRetIva1")]
    public decimal? BaseRetIva1 { get; set; }

    [Column("valorRetIva1")]
    public decimal? ValorRetIva1 { get; set; }

    [Column("tipoRetIva1")]
    public string? TipoRetIva1 { get; set; }

    [Column("idRetRenta1")]
    public string? IdRetRenta1 { get; set; }

    [Column("descripcionRetRenta1")]
    public string? DescripcionRetRenta1 { get; set; }

    [Column("baseRetRenta1")]
    public decimal? BaseRetRenta1 { get; set; }

    [Column("valorRetRenta1")]
    public decimal? ValorRetRenta1 { get; set; }

    [Column("tipoRetRenta1")]
    public string? TipoRetRenta1 { get; set; }

    [Column("correoad")]
    public string? Correoad { get; set; }

    [Column("detalleextra")]
    public string? Detalleextra { get; set; }

    [StringLength(300)]
    public string? NombreXml { get; set; }
}
