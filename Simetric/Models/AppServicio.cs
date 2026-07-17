using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("APP_SERVICIOS")]
public class AppServicio
{
    [Key]
    public int ServicioId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Clave { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Descripcion { get; set; }

    [MaxLength(150)]
    public string? RutaAcceso { get; set; }

    public bool RequiereSuscripcion { get; set; }

    public bool Estado { get; set; } = true;

    public int OrdenVisual { get; set; }

    [MaxLength(50)]
    public string? Icono { get; set; }

    [MaxLength(20)]
    public string? ColorHex { get; set; }

    public DateTime FechaCreacion { get; set; }
}
