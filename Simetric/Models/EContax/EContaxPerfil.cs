using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Simetric.Models.EContax;

[Table("ECONTAX_PERFIL")]
public sealed class EContaxPerfil
{
    [Key] public int IdPerfil { get; set; }
    public int IdEmpresa { get; set; }
    public int? IdSucursal { get; set; }
    [MaxLength(200)] public string Nombre { get; set; } = string.Empty;
    public string MenusJson { get; set; } = "[]";
    public string? AccionesJson { get; set; }
    public bool Estado { get; set; } = true;
}

[Flags]
public enum EContaxAccion
{
    Ver = 1, Crear = 2, Editar = 4, Eliminar = 8, Exportar = 16,
    Todas = Ver | Crear | Editar | Eliminar | Exportar
}

public static class EContaxPermisos
{
    public static Dictionary<int, EContaxAccion> LeerAcciones(string? json) => json is null
        ? new() : JsonSerializer.Deserialize<Dictionary<int, EContaxAccion>>(json) ?? new();

    public static EContaxAccion AccionesDe(string? json, int menuId)
    {
        var acciones = json is null ? EContaxAccion.Todas : LeerAcciones(json).GetValueOrDefault(menuId);
        return Normalizar(acciones);
    }

    public static EContaxAccion Normalizar(EContaxAccion acciones) =>
        acciones.HasFlag(EContaxAccion.Ver) && (acciones & ~EContaxAccion.Todas) == 0 ? acciones : 0;
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
