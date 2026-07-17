using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("rol", Schema = "dbo")]
public class EContaxRol
{
    [Key]
    [Column("id_rol")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdRol { get; set; }

    [Column("nombre_rol")]
    [StringLength(200)]
    public string? NombreRol { get; set; }

    [Column("permiso_rol")]
    public int? PermisoRol { get; set; }

    [Column("estado_rol")]
    public int? EstadoRol { get; set; } = 1;

    [NotMapped]
    public bool EstaActivo => EstadoRol == 1;
}
