using System.ComponentModel.DataAnnotations;

namespace Simetric.Models;

public partial class Codigosimpuesto
{
    [Key]
    [Required(ErrorMessage = "El código es obligatorio")]
    [StringLength(10, ErrorMessage = "Máximo 10 caracteres")]
    [RegularExpression(@"^[A-Za-z0-9]+$", ErrorMessage = "Solo letras y números")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "La descripción es obligatoria")]
    [StringLength(50, ErrorMessage = "Máximo 50 caracteres")]
    [RegularExpression(@"^[A-Za-zÁÉÍÓÚáéíóúÑñ\s]+$", ErrorMessage = "Solo letras y espacios")]
    public string? Descripcion { get; set; }

    [RegularExpression(@"^[AI]$", ErrorMessage = "Estado inválido")]
    public string? Estado { get; set; } = "A";

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
}