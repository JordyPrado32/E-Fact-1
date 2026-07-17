using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxOrganizacionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenantService;

    public EContaxOrganizacionService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenantService)
    {
        _dbFactory = dbFactory;
        _tenantService = tenantService;
    }

    public async Task<List<EContaxEmpresaDto>> GetEmpresasAsync(bool includeInactive = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var query = context.EContaxEmpresas.AsNoTracking();

        if (!includeInactive)
            query = query.Where(x => x.Estado);

        return await query
            .OrderBy(x => x.Nombre)
            .Select(x => new EContaxEmpresaDto
            {
                IdEmpresa = x.IdEmpresa,
                Nombre = x.Nombre,
                Ruc = x.Ruc,
                Estado = x.Estado
            })
            .ToListAsync();
    }

    public async Task<List<EContaxSucursalDto>> GetSucursalesAsync(int? empresaId = null, bool includeInactive = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var query = context.EContaxSucursales.AsNoTracking();

        if (empresaId is > 0)
            query = query.Where(x => x.IdEmpresa == empresaId.Value);

        if (!includeInactive)
            query = query.Where(x => x.Estado);

        return await query
            .OrderBy(x => x.Nombre)
            .ThenBy(x => x.IdSucursal)
            .Select(x => new EContaxSucursalDto
            {
                IdSucursal = x.IdSucursal,
                IdEmpresa = x.IdEmpresa,
                Nombre = x.Nombre,
                Codigo = x.Codigo,
                Direccion = x.Direccion,
                Estado = x.Estado
            })
            .ToListAsync();
    }

    public async Task<EContaxOrganizacionResumenDto> GetResumenAsync(int actorUserId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);

        var empresa = await context.EContaxEmpresas
            .AsNoTracking()
            .Where(x => x.IdEmpresa == actor.IdEmpresa)
            .Select(x => new EContaxEmpresaDto
            {
                IdEmpresa = x.IdEmpresa,
                Nombre = x.Nombre,
                Ruc = x.Ruc,
                Estado = x.Estado
            })
            .FirstOrDefaultAsync() ?? new EContaxEmpresaDto
            {
                IdEmpresa = actor.IdEmpresa,
                Nombre = $"Empresa {actor.IdEmpresa}",
                Estado = true
            };

        var sucursales = await context.EContaxSucursales
            .AsNoTracking()
            .Where(x => x.IdEmpresa == actor.IdEmpresa)
            .OrderBy(x => x.Nombre)
            .ThenBy(x => x.IdSucursal)
            .Select(x => new EContaxSucursalResumenDto
            {
                IdSucursal = x.IdSucursal,
                IdEmpresa = x.IdEmpresa,
                Nombre = x.Nombre,
                Codigo = x.Codigo,
                Direccion = x.Direccion,
                Estado = x.Estado,
                ProductosActivos = context.Productos.Count(p =>
                    p.Idempresa == actor.IdEmpresa &&
                    p.Idsucursal == x.IdSucursal &&
                    p.Estado == true)
            })
            .ToListAsync();

        var clientesActivos = await context.Clientes.CountAsync(c =>
            c.Idempresa == actor.IdEmpresa &&
            c.Estado == true &&
            c.Numeroidentificacion != "9999999999999");

        var productosActivos = await context.Productos.CountAsync(p =>
            p.Idempresa == actor.IdEmpresa &&
            p.Estado == true);

        return new EContaxOrganizacionResumenDto
        {
            Contexto = EContaxTenantService.ToScopeDto(actor),
            Empresa = empresa,
            Sucursales = sucursales,
            ClientesActivos = clientesActivos,
            ProductosActivos = productosActivos
        };
    }

    public async Task GuardarEmpresaActualAsync(int actorUserId, EContaxEmpresaUpsertDto dto)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);

        if (!actor.EsJefeEmpresa)
            throw new InvalidOperationException("Solo el jefe de empresa puede actualizar la empresa.");

        NormalizarEmpresa(dto);

        if (string.IsNullOrWhiteSpace(dto.Nombre))
            throw new InvalidOperationException("El nombre de la empresa es obligatorio.");

        var empresa = await context.EContaxEmpresas.FirstOrDefaultAsync(x => x.IdEmpresa == actor.IdEmpresa);
        if (empresa is null)
        {
            empresa = new EContaxEmpresa
            {
                IdEmpresa = actor.IdEmpresa,
                FechaCreacion = DateTime.UtcNow
            };
            context.EContaxEmpresas.Add(empresa);
        }

        empresa.Nombre = dto.Nombre;
        empresa.Ruc = dto.Ruc;
        empresa.Estado = true;
        empresa.FechaActualizacion = DateTime.UtcNow;

        await AsegurarContextoJefeAsync(context, actor);
        await context.SaveChangesAsync();
    }

    public async Task<int> GuardarSucursalAsync(int actorUserId, EContaxSucursalUpsertDto dto)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);

        if (!actor.EsJefeEmpresa)
            throw new InvalidOperationException("Solo el jefe de empresa puede administrar sucursales.");

        NormalizarSucursal(dto);

        if (string.IsNullOrWhiteSpace(dto.Nombre))
            throw new InvalidOperationException("El nombre de la sucursal es obligatorio.");

        await AsegurarEmpresaBaseAsync(context, actor);
        await AsegurarContextoJefeAsync(context, actor);

        var nombreExiste = await context.EContaxSucursales.AnyAsync(x =>
            x.IdEmpresa == actor.IdEmpresa &&
            x.IdSucursal != dto.IdSucursal &&
            x.Nombre == dto.Nombre &&
            x.Estado);

        if (nombreExiste)
            throw new InvalidOperationException("Ya existe una sucursal activa con ese nombre en la empresa.");

        if (!string.IsNullOrWhiteSpace(dto.Codigo))
        {
            var codigoExiste = await context.EContaxSucursales.AnyAsync(x =>
                x.IdEmpresa == actor.IdEmpresa &&
                x.IdSucursal != dto.IdSucursal &&
                x.Codigo == dto.Codigo &&
                x.Estado);

            if (codigoExiste)
                throw new InvalidOperationException("Ya existe una sucursal activa con ese codigo en la empresa.");
        }

        EContaxSucursal? sucursal = null;
        if (dto.IdSucursal > 0)
        {
            sucursal = await context.EContaxSucursales
                .FirstOrDefaultAsync(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == dto.IdSucursal);
        }

        if (sucursal is null)
        {
            var siguienteId = (await context.EContaxSucursales
                .Where(x => x.IdEmpresa == actor.IdEmpresa)
                .MaxAsync(x => (int?)x.IdSucursal) ?? 0) + 1;

            sucursal = new EContaxSucursal
            {
                IdEmpresa = actor.IdEmpresa,
                IdSucursal = siguienteId,
                FechaCreacion = DateTime.UtcNow
            };
            context.EContaxSucursales.Add(sucursal);
        }

        sucursal.Nombre = dto.Nombre;
        sucursal.Codigo = dto.Codigo;
        sucursal.Direccion = dto.Direccion;
        sucursal.Estado = true;
        sucursal.FechaActualizacion = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return sucursal.IdSucursal;
    }

    public async Task CambiarEstadoSucursalAsync(int actorUserId, int sucursalId, bool estado)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);

        if (!actor.EsJefeEmpresa)
            throw new InvalidOperationException("Solo el jefe de empresa puede administrar sucursales.");

        var sucursal = await context.EContaxSucursales
            .FirstOrDefaultAsync(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursalId);

        if (sucursal is null)
            throw new InvalidOperationException("Sucursal no encontrada.");

        sucursal.Estado = estado;
        sucursal.FechaActualizacion = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task<List<EContaxUserAssignmentDto>> GetUsuariosContextoAsync(int actorUserId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);
        var userIds = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == actor.IdUsuarioTitular || (u.idJefe == actor.IdUsuarioTitular && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();

        return await context.EContaxUsuariosContexto
            .AsNoTracking()
            .Where(x => x.Estado && userIds.Contains(x.IdUsuario))
            .Select(x => new EContaxUserAssignmentDto
            {
                IdUsuario = x.IdUsuario,
                IdEmpresa = x.IdEmpresa,
                NombreEmpresa = x.Empresa != null ? x.Empresa.Nombre : string.Empty,
                IdSucursal = x.IdSucursal,
                NombreSucursal = x.Sucursal != null ? x.Sucursal.Nombre : null
            })
            .ToListAsync();
    }

    public async Task AsignarUsuarioAsync(int actorUserId, int targetUserId, int empresaId, int? sucursalId)
    {
        if (targetUserId <= 0 || empresaId <= 0)
            throw new InvalidOperationException("Debe seleccionar empresa y usuario validos.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);

        if (!actor.EsJefeEmpresa && actor.IdUsuario != targetUserId)
            throw new InvalidOperationException("Solo el jefe de empresa puede asignar usuarios.");

        var target = await context.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == targetUserId);
        if (target is null)
            throw new InvalidOperationException("Usuario no encontrado.");

        var perteneceCuenta = target.IdUsuario == actor.IdUsuarioTitular ||
            target.idJefe == actor.IdUsuarioTitular ||
            target.IdUsuario == actor.IdUsuario;

        if (!perteneceCuenta)
            throw new InvalidOperationException("El usuario no pertenece a la empresa actual.");

        if (empresaId != actor.IdEmpresa)
            throw new InvalidOperationException("No puedes asignar usuarios a otra empresa.");

        if (sucursalId is > 0)
        {
            var sucursalOk = await context.EContaxSucursales
                .AsNoTracking()
                .AnyAsync(x => x.IdSucursal == sucursalId.Value && x.IdEmpresa == empresaId && x.Estado);

            if (!sucursalOk)
                throw new InvalidOperationException("La sucursal seleccionada no pertenece a la empresa.");
        }
        else if (targetUserId != actor.IdUsuarioTitular)
        {
            throw new InvalidOperationException("Los usuarios de sucursal deben tener una sucursal asignada.");
        }

        var asignacion = await context.EContaxUsuariosContexto
            .FirstOrDefaultAsync(x => x.IdUsuario == targetUserId);

        if (asignacion is null)
        {
            asignacion = new EContaxUsuarioContexto
            {
                IdUsuario = targetUserId,
                FechaCreacion = DateTime.UtcNow
            };
            context.EContaxUsuariosContexto.Add(asignacion);
        }

        asignacion.IdEmpresa = empresaId;
        asignacion.IdSucursal = sucursalId is > 0 ? sucursalId : null;
        asignacion.Estado = true;
        asignacion.FechaActualizacion = DateTime.UtcNow;

        if (target.IdUsuario != actor.IdUsuarioTitular)
        {
            target.idJefe = actor.IdUsuarioTitular;
            target.estadoAsociado = true;
        }

        await context.SaveChangesAsync();
    }

    private static void NormalizarEmpresa(EContaxEmpresaUpsertDto dto)
    {
        dto.Nombre = (dto.Nombre ?? string.Empty).Trim();
        dto.Ruc = string.IsNullOrWhiteSpace(dto.Ruc) ? null : dto.Ruc.Trim();
    }

    private static void NormalizarSucursal(EContaxSucursalUpsertDto dto)
    {
        dto.Nombre = (dto.Nombre ?? string.Empty).Trim();
        dto.Codigo = string.IsNullOrWhiteSpace(dto.Codigo) ? null : dto.Codigo.Trim();
        dto.Direccion = string.IsNullOrWhiteSpace(dto.Direccion) ? null : dto.Direccion.Trim();
    }

    private static async Task AsegurarEmpresaBaseAsync(AppDbContext context, EContaxUserContext actor)
    {
        var empresaExiste = await context.EContaxEmpresas.AnyAsync(x => x.IdEmpresa == actor.IdEmpresa);
        if (empresaExiste)
            return;

        context.EContaxEmpresas.Add(new EContaxEmpresa
        {
            IdEmpresa = actor.IdEmpresa,
            Nombre = $"Empresa {actor.IdEmpresa}",
            Estado = true,
            FechaCreacion = DateTime.UtcNow
        });
    }

    private static async Task AsegurarContextoJefeAsync(AppDbContext context, EContaxUserContext actor)
    {
        var asignacion = await context.EContaxUsuariosContexto
            .FirstOrDefaultAsync(x => x.IdUsuario == actor.IdUsuarioTitular);

        if (asignacion is null)
        {
            asignacion = new EContaxUsuarioContexto
            {
                IdUsuario = actor.IdUsuarioTitular,
                FechaCreacion = DateTime.UtcNow
            };
            context.EContaxUsuariosContexto.Add(asignacion);
        }

        asignacion.IdEmpresa = actor.IdEmpresa;
        asignacion.IdSucursal = null;
        asignacion.Estado = true;
        asignacion.FechaActualizacion = DateTime.UtcNow;
    }
}
