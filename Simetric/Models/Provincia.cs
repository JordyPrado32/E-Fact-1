using System.ComponentModel.DataAnnotations;

namespace Simetric.Models;

public partial class Provincia
{

    [Key]
    public int IdProvincia { get; set; }
    public int? IdPais { get; set; }
    public string? Descripcion { get; set; }

    public virtual Pais? Pais { get; set; }
    public virtual ICollection<Ciudad> Ciudades { get; set; } = new List<Ciudad>();
    public virtual ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
}
