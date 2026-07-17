namespace Simetric.Models;

public partial class Pais
{
    public int IdPais { get; set; }
    public string? Descripcion { get; set; }

    public virtual ICollection<Provincia> Provincias { get; set; } = new List<Provincia>();
    public virtual ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
}
