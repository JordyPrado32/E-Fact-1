using Simetric.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("Productosubtipo")]
public partial class Productosubtipo
{
    [Key]
    public int Idsubtipo { get; set; }

    public int Idtipoproducto { get; set; }

    public string? Descripcion { get; set; }

    [Column("IDUSUARIO")]
    public int? Idusuario { get; set; }
    public string? Estado { get; set; }

    public string? Cuentacontable { get; set; }

    // --- LA SOLUCIÓN ESTÁ AQUÍ ---
    // Le decimos que esta navegación usa la columna 'Idtipoproducto'
    [ForeignKey("Idtipoproducto")]
    public virtual Productotipo IdtipoproductoNavigation { get; set; } = null!;

    public virtual ICollection<Producto> Productos { get; set; } = new List<Producto>();
}
