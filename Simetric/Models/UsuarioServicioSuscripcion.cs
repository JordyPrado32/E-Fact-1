using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("USUARIO_SERVICIO_SUSCRIPCION")]
public class UsuarioServicioSuscripcion
{
    [Key]
    public int SuscripcionId { get; set; }

    public int IdUsuario { get; set; }

    public int ServicioId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Estado { get; set; } = "PENDIENTE_PAGO";

    public DateTime? FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public bool EsVitalicia { get; set; }

    [MaxLength(250)]
    public string? Observacion { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaActualizacion { get; set; }

    [MaxLength(50)]
    public string? PlanActual { get; set; }

    [MaxLength(20)]
    public string? CicloActual { get; set; }

    [MaxLength(50)]
    public string? PlanAgendado { get; set; }

    [MaxLength(20)]
    public string? CicloAgendado { get; set; }
}
