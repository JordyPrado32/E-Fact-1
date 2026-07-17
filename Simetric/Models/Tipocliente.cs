using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("TIPOCLIENTE")]
    public partial class Tipocliente
    {
        [Key]
        [Column("TCL_CODIGO")]
        public int TclCodigo { get; set; }

        [Column("TCL_SEC")]
        public int TclSec { get; set; }

        [Column("TCL_DESCRIPCION")]
        public string? TclDescripcion { get; set; }

        public virtual ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    }
}