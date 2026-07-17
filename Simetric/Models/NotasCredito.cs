using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("NOTASCREDITO")]
    public class NotaCredito
    {
        [Key]
        [Column("sec")]
        public int Sec { get; set; }

        [Column("codClientes")]
        public int? CodClientes { get; set; }

        [Column("codEmisor")]
        public int? CodEmisor { get; set; }


        // Referencia a factura/documento modificado

        [Column("codDocumento")]
        public string? CodDocumento { get; set; }

        [Column("idDocModificado")]
        public int? IdDocModificado { get; set; }

        [Column("numDocModificado")]
        public string? NumDocModificado { get; set; }

        [Column("codDocModificado")]
        public string? CodDocModificado { get; set; }

        [Column("fechaEmiDocModificado")]
        public DateTime? FechaEmiDocModificado { get; set; }

        // Datos NC
        [Column("serie")]
        public string? Serie { get; set; }

        [Column("numNotaCredito")]
        public string? NumNotaCredito { get; set; }

        [Column("codClave")]
        public string? CodClave { get; set; }

        [Column("fchAutorizacion")]
        public DateTime? FchAutorizacion { get; set; }

        [Column("numAutorización")]
        public string? NumAutorizacion { get; set; }

        [Column("fechaAutoSRI")]
        public string? FechaAutoSri { get; set; }

        [Column("motivo")]
        public string? Motivo { get; set; }

        [Column("observacion")]
        public string? Observacion { get; set; }

        // Totales
        [Column("subtotal")]
        public decimal? Subtotal { get; set; }

        [Column("descuentos")]
        public decimal? Descuentos { get; set; }

        [Column("iva")]
        public decimal? Iva { get; set; }

        [Column("valorTotal")]
        public decimal? ValorTotal { get; set; }

        // Auditoría / usuario
        [Column("usuario")]
        public int? Usuario { get; set; }

        [Column("estado")]
        public bool? Estado { get; set; }

        [Column("autorizado")]
        public string? Autorizado { get; set; }

        [Column("correoad")]
        public string? Correoad { get; set; }

        [Column("detalleextra")]
        public string? Detalleextra { get; set; }
    }
}
