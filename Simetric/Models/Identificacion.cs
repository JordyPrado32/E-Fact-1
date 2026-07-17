using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("IDENTIFICACION")]
public partial class Identificacion
{
    [Key]
    [Column("Ide_Sec")]
    public int IdeSec { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(10, ErrorMessage = "El código no puede exceder 10 caracteres.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "El código solo permite números.")]
    [Column("Ide_Codigo")]
    public string IdeCodigo { get; set; } = null!;

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(80, ErrorMessage = "La descripción no puede exceder 80 caracteres.")]
    [Column("Ide_Descripcion")]
    public string? IdeDescripcion { get; set; }

    [Column("ESTADO")]
    public bool? Estado { get; set; }

    public virtual ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
}