using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

public sealed class EContaxSucursal
{
    public int IdSucursal { get; set; }

    public int IdEmpresa { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Codigo { get; set; }

    public string? Direccion { get; set; }

    public bool Estado { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaActualizacion { get; set; }

    [ForeignKey(nameof(IdEmpresa))]
    public EContaxEmpresa? Empresa { get; set; }
}
