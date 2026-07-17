namespace Simetric.DTOs.EContax;

public sealed class EContaxEmpresaDto
{
    public int IdEmpresa { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Ruc { get; set; }
    public bool Estado { get; set; }
}

public sealed class EContaxEmpresaUpsertDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Ruc { get; set; }
}

public class EContaxSucursalDto
{
    public int IdSucursal { get; set; }
    public int IdEmpresa { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Codigo { get; set; }
    public string? Direccion { get; set; }
    public bool Estado { get; set; }
}

public sealed class EContaxSucursalUpsertDto
{
    public int IdSucursal { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Codigo { get; set; }
    public string? Direccion { get; set; }
}

public sealed class EContaxSucursalResumenDto : EContaxSucursalDto
{
    public int ProductosActivos { get; set; }
}

public sealed class EContaxOrganizacionResumenDto
{
    public EContaxUserScopeDto Contexto { get; set; } = new();
    public EContaxEmpresaDto Empresa { get; set; } = new();
    public List<EContaxSucursalResumenDto> Sucursales { get; set; } = new();
    public int ClientesActivos { get; set; }
    public int ProductosActivos { get; set; }
}

public sealed class EContaxUserAssignmentDto
{
    public int IdUsuario { get; set; }
    public int IdEmpresa { get; set; }
    public string NombreEmpresa { get; set; } = string.Empty;
    public int? IdSucursal { get; set; }
    public string? NombreSucursal { get; set; }
}

public sealed class EContaxUserScopeDto
{
    public int IdUsuario { get; set; }
    public int IdUsuarioTitular { get; set; }
    public int IdEmpresa { get; set; }
    public int? IdSucursal { get; set; }
    public string Rol { get; set; } = string.Empty;
    public bool EsJefeEmpresa { get; set; }
    public bool PuedeFiltrarSucursal { get; set; }
}
