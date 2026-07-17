using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

public sealed class EContaxEmpresa
{
    [Key]
    public int IdEmpresa { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Ruc { get; set; }

    public bool Estado { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaActualizacion { get; set; }

    [InverseProperty(nameof(EContaxSucursal.Empresa))]
    public ICollection<EContaxSucursal> Sucursales { get; set; } = new List<EContaxSucursal>();
}
