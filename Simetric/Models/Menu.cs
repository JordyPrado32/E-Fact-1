using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models
{
    [Table("MENUS")]
    public class Menu
    {
        [Key]
        [Column("IDMENU")]
        public int IdMenu { get; set; }
        [Column("IDMENUPADRE")]
        public int IdMenuPadre { get; set; }

        [Column("NOMBREMENU")]
        [StringLength(100)]
        [Required(ErrorMessage = "El nombre del menú es obligatorio")]
        public string NombreMenu { get; set; } = string.Empty;

        [Column("ESTADOMENU")]
        public bool? EstadoMenu { get; set; } = true;

        [Column("RUTAMENU")]
        [StringLength(200)]
        public string? RutaMenu { get; set; }

        [Column("ICONOMENU")]
        [StringLength(50)]
        public string? IconoMenu { get; set; }

        [Column("orden_menu")]
        public int? OrdenMenu { get; set; }

        // Propiedad para saber si está seleccionado en la interfaz de "vistos"
        // No está en la DB, por eso usamos [NotMapped]
        [NotMapped]
        public bool IsSelected { get; set; }
    }
}