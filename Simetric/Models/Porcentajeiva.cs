using System.ComponentModel.DataAnnotations;

namespace Simetric.Models;

public partial class Porcentajeiva
{
    [Key]
    [Required(ErrorMessage = "El código es obligatorio")]
    [StringLength(10)]
    [RegularExpression(@"^\d+$", ErrorMessage = "Solo números")]
    public string Codigo { get; set; } = null!;

    [Required(ErrorMessage = "La descripción es obligatoria")]
    [StringLength(50)]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "El valor es obligatorio")]
    [RegularExpression(@"^\d+(\.\d+)?$", ErrorMessage = "Debe ser numérico")]
    public string? Valor { get; set; }

    [Range(0, 1, ErrorMessage = "Debe ser entre 0 y 1")]
    public decimal? ValorCalculo { get; set; }

    [RegularExpression(@"^[AI]$", ErrorMessage = "Estado inválido")]
    public string? Estado { get; set; } = "A";

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
}