using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_PORTAL_CONFIG")]
public sealed class AliadoPortalConfiguracion
{
    [Key]
    public int IdConfiguracion { get; set; }

    public decimal PorcentajeVentaNueva { get; set; } = 30m;
    public decimal PorcentajeRenovacionAliado { get; set; } = 30m;
    public decimal PorcentajeRenovacionNumerica { get; set; } = 15m;
    public decimal PorcentajeVentaDirecta { get; set; }
    public int DiasIntervencionNumerica { get; set; } = 15;
}
