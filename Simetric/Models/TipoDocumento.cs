using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("TIPODOCUMENTO")]
public partial class TipoDocumento
{
    [Key]
    [Column("sec")]
    public int Sec { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(10, ErrorMessage = "Máximo 10 caracteres.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "El código solo puede contener números.")]
    [Column("codigo")]
    public string? Codigo { get; set; }

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(150, ErrorMessage = "Máximo 150 caracteres.")]
    [Column("descripcion")]
    public string? Descripcion { get; set; }
}