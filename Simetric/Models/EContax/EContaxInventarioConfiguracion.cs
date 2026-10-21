using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_INVENTARIO_CONFIGURACION")]
public sealed class EContaxInventarioConfiguracion
{
    [Key]
    public int IdEmpresa { get; set; }

    [MaxLength(20)]
    public string MetodoValoracion { get; set; } = "PROMEDIO";

    public int? IdUsuarioActualizacion { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
