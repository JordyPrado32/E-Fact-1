using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Simetric.Models;

public partial class TipoUsuario
{
    [Key]
    public int IdTipoUsuario { get; set; }

    public string NombreTipo { get; set; } = null!;

    public string? Descripcion { get; set; }

    public bool? Estado { get; set; }

    public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
}
