using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_LIQUIDACION_FACTURA")]
public sealed class AliadoLiquidacionFactura
{
    [Key] public int IdRevision { get; set; }
    public int IdLiquidacion { get; set; }
    [MaxLength(300)] public string ArchivoUrl { get; set; } = string.Empty;
    [MaxLength(180)] public string NombreArchivo { get; set; } = string.Empty;
    [MaxLength(80)] public string TipoArchivo { get; set; } = string.Empty;
    [MaxLength(30)] public string Estado { get; set; } = "Pendiente";
    [MaxLength(500)] public string? Observacion { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.Now;
    public int IdUsuarioRegistro { get; set; }
    public DateTime? FechaRevision { get; set; }
    public int? IdUsuarioRevision { get; set; }
}
