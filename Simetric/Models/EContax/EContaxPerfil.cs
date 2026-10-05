using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Simetric.Models.EContax;

[Table("ECONTAX_PERFIL")]
public sealed class EContaxPerfil
{
    [Key] public int IdPerfil { get; set; }
    public int IdEmpresa { get; set; }
    public int? IdSucursal { get; set; }
    [MaxLength(200)] public string Nombre { get; set; } = string.Empty;
    public string MenusJson { get; set; } = "[]";
    public bool Estado { get; set; } = true;
}

[Table("ECONTAX_INVITACION")]
public sealed class EContaxInvitacion
{
    [Key] public int IdInvitacion { get; set; }
    public int IdEmpresa { get; set; }
    public int IdSucursal { get; set; }
    public int IdPerfil { get; set; }
    public int IdInvitador { get; set; }
    public bool EsAdminSucursal { get; set; }
    [MaxLength(320)] public string Email { get; set; } = string.Empty;
    [MaxLength(64)] public string TokenHash { get; set; } = string.Empty;
    public DateTime Vence { get; set; }
    public DateTime? Aceptada { get; set; }
    public bool Cancelada { get; set; }
}
