using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models;

[Table("ALIADO_PORTAL_ROL")]
public sealed class AliadoPortalRol
{
    [Key] public int IdRol { get; set; }
    [Required, MaxLength(80)] public string Nombre { get; set; } = string.Empty;
    [MaxLength(250)] public string? Descripcion { get; set; }
    public bool Activo { get; set; }
}

[Table("ALIADO_PORTAL_MENU")]
public sealed class AliadoPortalMenu
{
    [Key] public int IdMenu { get; set; }
    [Required, MaxLength(100)] public string Nombre { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Ruta { get; set; } = string.Empty;
    [MaxLength(50)] public string? Icono { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

[Table("ALIADO_PORTAL_ROL_MENU")]
public sealed class AliadoPortalRolMenu
{
    [Key] public int IdRolMenu { get; set; }
    public int IdRol { get; set; }
    public int IdMenu { get; set; }
}

[Table("ALIADO_PORTAL_USUARIO_ROL")]
public sealed class AliadoPortalUsuarioRol
{
    [Key] public int IdUsuarioRol { get; set; }
    public int IdUsuario { get; set; }
    public int IdRol { get; set; }
}
