using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("ROL_MENU")]
    public class RolMenu
    {
        [Column("IDROL")]
        public int IdRol { get; set; }

        [Column("IDMENU")]
        public int IdMenu { get; set; }

        // Propiedades de navegación opcionales
        public virtual Rol? Rol { get; set; }
        public virtual Menu? Menu { get; set; }
    }
}