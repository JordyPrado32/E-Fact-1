using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxBodegaService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EContaxTenantService _tenantService;

    public EContaxBodegaService(
        IDbContextFactory<AppDbContext> dbFactory,
        EContaxTenantService tenantService)
    {
        _dbFactory = dbFactory;
        _tenantService = tenantService;
    }

    public async Task<List<EContaxBodegaDto>> ListarAsync(int actorUserId, int? sucursalId = null)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);
        var query = context.EContaxBodegas
            .AsNoTracking()
            .Where(x => x.IdEmpresa == actor.IdEmpresa);

        if (!actor.EsJefeEmpresa)
            query = query.Where(x => x.IdSucursal == actor.IdSucursal);
        else if (sucursalId is > 0)
            query = query.Where(x => x.IdSucursal == sucursalId.Value);

        return await query
            .OrderBy(x => x.IdSucursal)
            .ThenBy(x => x.Nombre)
            .Select(x => new EContaxBodegaDto
            {
                IdBodega = x.IdBodega,
                IdEmpresa = x.IdEmpresa,
                IdSucursal = x.IdSucursal,
                Nombre = x.Nombre,
                Codigo = x.Codigo,
                Direccion = x.Direccion,
                Observacion = x.Observacion,
                NombreSucursal = x.Sucursal != null ? x.Sucursal.Nombre : string.Empty,
                Estado = x.Estado
            })
            .ToListAsync();
    }

    public async Task<int> GuardarAsync(int actorUserId, EContaxBodegaUpsertDto dto)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);
        Normalizar(dto);

        if (string.IsNullOrWhiteSpace(dto.Nombre))
            throw new InvalidOperationException("El nombre de la bodega es obligatorio.");
        if (string.IsNullOrWhiteSpace(dto.Codigo))
            throw new InvalidOperationException("El código de la bodega es obligatorio.");

        var sucursalId = dto.IdSucursal > 0 ? dto.IdSucursal : actor.IdSucursal;
        if (sucursalId is not > 0)
            throw new InvalidOperationException("Selecciona la sucursal de la bodega.");

        await ValidarAdministradorAsync(context, actor, sucursalId.Value);

        var sucursalExiste = await context.EContaxSucursales.AnyAsync(x =>
            x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursalId.Value && x.Estado);
        if (!sucursalExiste)
            throw new InvalidOperationException("La sucursal seleccionada no pertenece a la empresa o está inactiva.");

        var nombreExiste = await context.EContaxBodegas.AnyAsync(x =>
            x.IdEmpresa == actor.IdEmpresa &&
            x.IdSucursal == sucursalId.Value &&
            x.IdBodega != dto.IdBodega &&
            x.Nombre == dto.Nombre);
        if (nombreExiste)
            throw new InvalidOperationException("Ya existe una bodega con ese nombre en la sucursal.");

        var codigoExiste = await context.EContaxBodegas.AnyAsync(x =>
            x.IdEmpresa == actor.IdEmpresa &&
            x.IdSucursal == sucursalId.Value &&
            x.IdBodega != dto.IdBodega &&
            x.Codigo == dto.Codigo);
        if (codigoExiste)
            throw new InvalidOperationException("Ya existe una bodega con ese código en la sucursal.");

        var bodega = dto.IdBodega > 0
            ? await context.EContaxBodegas.FirstOrDefaultAsync(x =>
                x.IdBodega == dto.IdBodega && x.IdEmpresa == actor.IdEmpresa)
            : null;

        if (bodega is null)
        {
            bodega = new EContaxBodega
            {
                IdEmpresa = actor.IdEmpresa,
                FechaCreacion = DateTime.UtcNow
            };
            context.EContaxBodegas.Add(bodega);
        }

        bodega.IdSucursal = sucursalId.Value;
        bodega.Nombre = dto.Nombre;
        bodega.Codigo = dto.Codigo;
        bodega.Direccion = dto.Direccion;
        bodega.Observacion = dto.Observacion;
        bodega.Estado = true;
        bodega.FechaActualizacion = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return bodega.IdBodega;
    }

    public async Task CambiarEstadoAsync(int actorUserId, int idBodega, bool estado)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await _tenantService.GetContextAsync(context, actorUserId);
        var bodega = await context.EContaxBodegas.FirstOrDefaultAsync(x =>
            x.IdBodega == idBodega && x.IdEmpresa == actor.IdEmpresa);

        if (bodega is null)
            throw new InvalidOperationException("Bodega no encontrada.");

        await ValidarAdministradorAsync(context, actor, bodega.IdSucursal);
        bodega.Estado = estado;
        bodega.FechaActualizacion = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private static async Task ValidarAdministradorAsync(
        AppDbContext context,
        EContaxUserContext actor,
        int sucursalId)
    {
        if (actor.EsJefeEmpresa)
            return;

        if (actor.IdSucursal != sucursalId || !await context.EContaxUsuariosContexto.AnyAsync(x =>
                x.IdUsuario == actor.IdUsuario && x.Estado && x.EsAdminSucursal))
            throw new InvalidOperationException("Solo el administrador de la sucursal puede administrar sus bodegas.");
    }

    private static void Normalizar(EContaxBodegaUpsertDto dto)
    {
        dto.Nombre = (dto.Nombre ?? string.Empty).Trim();
        dto.Codigo = (dto.Codigo ?? string.Empty).Trim().ToUpperInvariant();
        dto.Direccion = string.IsNullOrWhiteSpace(dto.Direccion) ? null : dto.Direccion.Trim();
        dto.Observacion = string.IsNullOrWhiteSpace(dto.Observacion) ? null : dto.Observacion.Trim();
    }
}
