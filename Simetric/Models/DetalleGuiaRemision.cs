using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("DETALLESGUIAREMISION")]
    public class DetalleGuiaRemision
    {
        [Key]
        [Column("sec")]
        public int Sec { get; set; }

        [Column("idGuiaRemision")]
        public int? IdGuiaRemision { get; set; }

        [Column("codInterno")]
        public string? CodInterno { get; set; }

        [Column("codAdicional")]
        public string? CodAdicional { get; set; }

        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Column("cantidad")]
        public int? Cantidad { get; set; }
    }
}