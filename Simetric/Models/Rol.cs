using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("ROLES")]
    public class Rol
    {
        [Key]
        [Column("IDROL")]
        public int IdRol { get; set; }

        [Column("DESCRIPCIONROL")]
        [StringLength(100)]
        [Required(ErrorMessage = "La descripción del rol es obligatoria")]
        public string DescripcionRol { get; set; } = string.Empty;

        [Column("ESTADOROL")]
        public bool? EstadoRol { get; set; } = true;

        [Column("IDTIPOUSUARIO")]
        public int? IdTipoUsuario { get; set; }

        // Relación: Un rol tiene muchos menús
        public virtual ICollection<Menu> Menus { get; set; } = new List<Menu>();
    }
}