using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

public sealed class EContaxUsuarioContexto
{
    [Key]
    public int IdUsuario { get; set; }

    public int IdEmpresa { get; set; }

    public int? IdSucursal { get; set; }

    public bool Estado { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaActualizacion { get; set; }

    [ForeignKey(nameof(IdUsuario))]
    public Usuario? Usuario { get; set; }

    [ForeignKey(nameof(IdEmpresa))]
    public EContaxEmpresa? Empresa { get; set; }

    public EContaxSucursal? Sucursal { get; set; }
}
