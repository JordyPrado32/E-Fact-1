using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("menu", Schema = "dbo")]
public class EContaxMenu
{
    [Key]
    [Column("id_menu")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdMenu { get; set; }

    [Column("nombre_menu")]
    [StringLength(200)]
    public string? NombreMenu { get; set; }

    [Column("pertenece_menu")]
    public int? PerteneceMenu { get; set; }

    [Column("url_menu")]
    [StringLength(200)]
    public string? UrlMenu { get; set; }

    [Column("desc_menu")]
    [StringLength(300)]
    public string? DescripcionMenu { get; set; }

    [Column("icon_menu")]
    [StringLength(50)]
    public string? IconoMenu { get; set; }

    [Column("estado_menu")]
    public int? EstadoMenu { get; set; } = 0;

    [Column("id_padre")]
    public int? IdPadre { get; set; }

    [Column("orden_menu")]
    public int? OrdenMenu { get; set; }

    [NotMapped]
    public bool IsSelected { get; set; }
}

