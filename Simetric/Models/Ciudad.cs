namespace Simetric.Models;

public partial class Ciudad
{
    public int IdCiudad { get; set; }
    public int? IdProvincia { get; set; }
    public string? Descripcion { get; set; }

    public virtual Provincia? Provincia { get; set; }
    public virtual ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
}
