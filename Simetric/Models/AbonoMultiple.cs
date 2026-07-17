using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("ABONOMULTIPLE")]
    public class AbonoMultiple
    {
        [Key]
        public int sec { get; set; }
        public int? idAbono { get; set; }
        public decimal? valor { get; set; }
        public string? origen { get; set; }
        public int? idBanco { get; set; }
        public DateTime? fechaPago { get; set; }
        public string? numDocumento { get; set; }
        public string? numDeposito { get; set; }
        public int? codFactura { get; set; }
        public int? idCliente { get; set; }
        public int? tipoAbono { get; set; }
        public DateTime? fechaTrs { get; set; }
        public string? observacion { get; set; }
        public string? formaPago { get; set; }
        public DateTime? fechaCheque { get; set; }
        public bool? estado { get; set; }
        public decimal? saldo { get; set; }
        public decimal? saldoUtilizado { get; set; }
    }
}
