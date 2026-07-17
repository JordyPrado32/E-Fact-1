using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("FORMASDEPAGO")]
public partial class FormasPago
{
    [Key]
    [Column("ID")]
    public int Id { get; set; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(2, ErrorMessage = "El código no puede tener más de 2 caracteres.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "El código solo puede contener números.")]
    [Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "La descripción SRI es obligatoria.")]
    [StringLength(150, ErrorMessage = "La descripción SRI no puede exceder 150 caracteres.")]
    [Column("descripcionSri")]
    public string? DescripcionSri { get; set; }

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(150, ErrorMessage = "La descripción no puede exceder 150 caracteres.")]
    [Column("descripcion")]
    public string? Descripcion { get; set; }

    [Column("tipoVenta")]
    public bool? TipoVenta { get; set; }

    [Column("tipoCompra")]
    public bool? TipoCompra { get; set; }

    [Column("estado")]
    public bool? Estado { get; set; }
}