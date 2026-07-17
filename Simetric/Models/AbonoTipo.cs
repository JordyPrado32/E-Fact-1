using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("ABONOTIPO")]
    public class AbonoTipo
    {
        public int sec { get; set; } // No es la PK según tu script

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // Es PK pero no Identity en tu script
        public int idTipoAbono { get; set; }

        public string descripcion { get; set; } = string.Empty;
        public bool? estado { get; set; }
    }
}
