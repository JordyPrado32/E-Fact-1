namespace Simetric.Models;

public partial class Cliente
{
    public virtual Pais? PaisNavegacion { get; set; }
    public virtual Provincia? ProvinciaNavegacion { get; set; }
    public virtual Ciudad? CiudadNavegacion { get; set; }
}
