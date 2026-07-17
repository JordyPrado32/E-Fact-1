using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("ABONOS")]
    public class Abonos
    {
        [Key]
        public int sec { get; set; }

        public int? codFactura { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? abono { get; set; }

        public string? formaPago { get; set; }
        public int? idBanco { get; set; }
        public DateTime? fechaPago { get; set; }
        public DateTime? fechaTrs { get; set; }
        public string? numDocumento { get; set; }
        public string? numAutorizacion { get; set; }
        public string? serie { get; set; }

        // AQUÍ ESTÁ LA CORRECCIÓN:
        // Mapeamos la columna 'base' de SQL a la propiedad 'BaseImporte' de C#
        [Column("base", TypeName = "decimal(18, 2)")]
        public decimal? BaseImporte { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? porcentaje { get; set; }

        public string? numDeposito { get; set; }
        public string? numTransferencia { get; set; }
        public int? idUsuario { get; set; }
        public DateTime? fechaCheque { get; set; }
        public int? idCobrador { get; set; }
        public string? observacion { get; set; }
        public bool? contabilizado { get; set; }
        public bool? estado { get; set; }
        public int? tipoAbono { get; set; }
        public int? idCliente { get; set; }
        public int? idAbonoMultiple { get; set; }
    }
}
