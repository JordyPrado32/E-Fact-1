using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("NOTASDEBITO", Schema = "dbo")]
public class NotaDebito
{
    [Key]
    [Column("sec")]
    public int Sec { get; set; }

    [Column("codClave")]
    public string? CodClave { get; set; }

    [Column("codClientes")]
    public int? CodClientes { get; set; }

    [Column("codEmisor")]
    public int? CodEmisor { get; set; }

    [Column("codRespuesta")]
    public int? CodRespuesta { get; set; }

    [Column("fchAutorizacion")]
    public DateTime? FchAutorizacion { get; set; }

    [Column("numNotaDebito")]
    public string? NumNotaDebito { get; set; }

    [Column("numAutorización")]
    public string? NumAutorizacion { get; set; }

    [Column("codDocumento")]
    public string? CodDocumento { get; set; }

    [Column("subtotal", TypeName = "decimal(18,2)")]
    public decimal? Subtotal { get; set; }

    [Column("valorTotal", TypeName = "decimal(18,2)")]
    public decimal? ValorTotal { get; set; }

    [Column("fechaVence")]
    public DateTime? FechaVence { get; set; }

    [Column("usuario")]
    public int? Usuario { get; set; }

    [StringLength(1)]
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
    public string? FechaAutoSri { get; set; }

    [Column("motivo")]
    public string? Motivo { get; set; }

    [Column("codDocModificado")]
    public string? CodDocModificado { get; set; }

    [Column("numDocModificado")]
    public string? NumDocModificado { get; set; }

    [Column("idDocModificado")]
    public int? IdDocModificado { get; set; }

    [Column("fechaEmiDocModificado")]
    public DateTime? FechaEmiDocModificado { get; set; }

    [Column("subtotal12", TypeName = "decimal(18,2)")]
    public decimal? Subtotal12 { get; set; }

    [Column("subtotal0", TypeName = "decimal(18,2)")]
    public decimal? Subtotal0 { get; set; }

    [Column("iva", TypeName = "decimal(18,2)")]
    public decimal? Iva { get; set; }

    [Column("noImp", TypeName = "decimal(18,2)")]
    public decimal? NoImp { get; set; }

    [Column("exIva", TypeName = "decimal(18,2)")]
    public decimal? ExIva { get; set; }

    [Column("descuentos", TypeName = "decimal(18,2)")]
    public decimal? Descuentos { get; set; }

    [Column("ambiente")]
    public int? Ambiente { get; set; }

    [StringLength(1)]
    [Column("estado")]
    public string? Estado { get; set; }
}
