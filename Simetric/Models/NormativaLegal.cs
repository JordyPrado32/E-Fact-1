using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("NORMATIVA_LEGAL", Schema = "dbo")]
public class NormativaLegal
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Codigo { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Titulo { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Categoria { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Resumen { get; set; } = string.Empty;

    [Required]
    public string Contenido { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? UrlOficial { get; set; }

    [MaxLength(40)]
    public string EstadoNorma { get; set; } = "Vigente";

    public DateTime? FechaPublicacion { get; set; }
    public DateTime? FechaVigencia { get; set; }
    public DateTime? FechaUltimaVerificacion { get; set; }
    public bool Activo { get; set; } = true;
    public int Orden { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    public DateTime FechaActualizacion { get; set; } = DateTime.Now;
}
