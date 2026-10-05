using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.DTOs.EContax;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public sealed class EContaxTenantService
{
    public const string RolJefeEmpresa = "JEFE_EMPRESA";
    public const string RolAdminSucursal = "ADMIN_SUCURSAL";
    public const string RolUsuarioSucursal = "USUARIO_SUCURSAL";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public EContaxTenantService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<EContaxUserContext> GetContextAsync(int userId)
    {
        if (userId <= 0)
            throw new InvalidOperationException("Sesion no valida.");

        await using var context = await _dbFactory.CreateDbContextAsync();
        return await GetContextAsync(context, userId);
    }

    public async Task<EContaxUserContext> GetContextAsync(AppDbContext context, int userId)
    {
        var usuario = await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == userId && u.Estado == true)
            .Select(u => new
            {
                u.IdUsuario,
                u.idJefe,
                u.estadoAsociado,
                u.NombreEmpresa
            })
            .FirstOrDefaultAsync();

        if (usuario is null)
            throw new InvalidOperationException("Usuario no encontrado.");

        var asignacion = await context.EContaxUsuariosContexto.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == userId);
        if (asignacion is not null)
        {
            if (!asignacion.Estado)
                throw new InvalidOperationException("La cuenta no está vinculada a una empresa de E-Contax.");
            var empresa = await context.EContaxEmpresas
                .SingleAsync(x => x.IdEmpresa == asignacion.IdEmpresa);
            if (!empresa.Estado)
                throw new InvalidOperationException("La empresa no está habilitada.");
            var emisorActivo = await context.Emisores
                .AsNoTracking()
                .Where(x => (x.IdEmpresa == asignacion.IdEmpresa || x.IdUsuario == userId) && x.Estado && x.IdUsuario.HasValue)
                .OrderBy(x => x.Codigo)
                .FirstOrDefaultAsync();
            if (empresa.IdTitular is null && emisorActivo?.IdUsuario is > 0)
            {
                empresa.IdTitular = emisorActivo.IdUsuario;
                if (!await context.EContaxSucursales.AnyAsync(x => x.IdEmpresa == empresa.IdEmpresa))
                    context.EContaxSucursales.Add(new EContaxSucursal { IdEmpresa = empresa.IdEmpresa, IdSucursal = emisorActivo.IdSucursal ?? 1, Nombre = "Matriz", Codigo = "001" });
                await context.SaveChangesAsync();
            }
            var titular = empresa.IdTitular ?? emisorActivo?.IdUsuario ?? 0;
            if (titular <= 0)
                throw new InvalidOperationException("La empresa no tiene un emisor activo con cuenta principal.");
            var jefe = titular == userId;
            if (!jefe && !await context.EContaxSucursales.AnyAsync(x => x.IdEmpresa == asignacion.IdEmpresa &&
                x.IdSucursal == asignacion.IdSucursal && x.Estado))
                throw new InvalidOperationException("La sucursal no está habilitada.");
            return new(userId, titular, asignacion.IdEmpresa, asignacion.IdSucursal,
                jefe ? RolJefeEmpresa : asignacion.EsAdminSucursal ? RolAdminSucursal : RolUsuarioSucursal, jefe);
        }

        var ownerId = usuario.estadoAsociado == true && usuario.idJefe is > 0
            ? usuario.idJefe.Value
            : usuario.IdUsuario;

        // La primera cuenta que entra con un emisor activo crea su propia empresa.
        // Nunca depende de una cuenta ajena para usar la licencia que adquirió.
        if (userId == ownerId)
        {
            var emisor = await context.Emisores
                .Where(x => x.IdUsuario == userId && x.Estado)
                .OrderBy(x => x.Codigo)
                .FirstOrDefaultAsync();
            if (emisor is not null)
            {
                var empresaIdInicial = emisor.IdEmpresa ?? userId;
                var sucursalIdInicial = emisor.IdSucursal ?? 1;
                emisor.IdEmpresa ??= empresaIdInicial;
                emisor.IdSucursal ??= sucursalIdInicial;
                var empresaInicial = await context.EContaxEmpresas.FindAsync(empresaIdInicial);
                if (empresaInicial is null)
                {
                    empresaInicial = new EContaxEmpresa { IdEmpresa = empresaIdInicial, Nombre = emisor.RazonSocial ?? $"Empresa {empresaIdInicial}", Ruc = emisor.Ruc, IdTitular = userId };
                    context.EContaxEmpresas.Add(empresaInicial);
                }
                else if (empresaInicial.IdTitular is null)
                    empresaInicial.IdTitular = userId;

                if (!await context.EContaxSucursales.AnyAsync(x => x.IdEmpresa == empresaIdInicial && x.IdSucursal == sucursalIdInicial))
                    context.EContaxSucursales.Add(new EContaxSucursal { IdEmpresa = empresaIdInicial, IdSucursal = sucursalIdInicial, Nombre = "Matriz", Codigo = "001" });

                context.EContaxUsuariosContexto.Add(new EContaxUsuarioContexto { IdUsuario = userId, IdEmpresa = empresaIdInicial, IdSucursal = sucursalIdInicial });
                await context.SaveChangesAsync();
                return new(userId, userId, empresaIdInicial, sucursalIdInicial, RolJefeEmpresa, true);
            }
        }

        var rol = await GetRolUsuarioAsync(context, userId, ownerId);
        var contexto = await context.EContaxUsuariosContexto
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == userId && x.Estado);

        contexto ??= await context.EContaxUsuariosContexto
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdUsuario == ownerId && x.Estado);

        var empresaId = contexto?.IdEmpresa ?? await ResolveEmpresaFallbackAsync(context, ownerId);
        var sucursalId = contexto?.IdSucursal ?? await ResolveSucursalFallbackAsync(context, userId, ownerId, empresaId);

        if (empresaId <= 0)
            throw new InvalidOperationException("El usuario no tiene empresa asignada para E-Contax.");

        var esJefe = EsRolJefeEmpresa(rol) || userId == ownerId && string.IsNullOrWhiteSpace(rol);
        if (esJefe)
            rol = RolJefeEmpresa;

        return new EContaxUserContext(
            userId,
            ownerId,
            empresaId,
            sucursalId > 0 ? sucursalId : null,
            rol,
            esJefe);
    }

    public async Task<List<EContaxSucursalDto>> GetSucursalesEmpresaAsync(int empresaId, bool includeInactive = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await GetSucursalesEmpresaAsync(context, empresaId, includeInactive);
    }

    public async Task<List<EContaxSucursalDto>> GetSucursalesEmpresaAsync(AppDbContext context, int empresaId, bool includeInactive = false)
    {
        var query = context.EContaxSucursales
            .AsNoTracking()
            .Where(x => x.IdEmpresa == empresaId);

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

    public async Task<int> ResolveTargetSucursalAsync(EContaxUserContext userContext, int? requestedSucursalId)
    {
        if (!userContext.EsJefeEmpresa)
        {
            if (userContext.IdSucursal is not > 0)
                throw new InvalidOperationException("El usuario no tiene sucursal asignada.");

            return userContext.IdSucursal.Value;
        }

        if (requestedSucursalId is not > 0)
        {
            await using var defaultContext = await _dbFactory.CreateDbContextAsync();
            var sucursalPredeterminada = await defaultContext.EContaxSucursales
                .AsNoTracking()
                .Where(x => x.IdEmpresa == userContext.IdEmpresa && x.Estado)
                .OrderBy(x => x.IdSucursal == 1 ? 0 : 1)
                .ThenBy(x => x.IdSucursal)
                .Select(x => x.IdSucursal)
                .FirstOrDefaultAsync();

            if (sucursalPredeterminada > 0)
                return sucursalPredeterminada;

            throw new InvalidOperationException("La empresa no tiene una sucursal activa.");
        }

        await using var context = await _dbFactory.CreateDbContextAsync();
        var pertenece = await context.EContaxSucursales
            .AsNoTracking()
            .AnyAsync(x =>
                x.IdSucursal == requestedSucursalId.Value &&
                x.IdEmpresa == userContext.IdEmpresa &&
                x.Estado);

        if (!pertenece)
            throw new InvalidOperationException("La sucursal seleccionada no pertenece a la empresa.");

        return requestedSucursalId.Value;
    }

    public async Task CambiarSucursalJefeAsync(int userId, int sucursalId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var actor = await GetContextAsync(context, userId);
        if (!actor.EsJefeEmpresa || !await context.EContaxSucursales.AnyAsync(x => x.IdEmpresa == actor.IdEmpresa && x.IdSucursal == sucursalId && x.Estado))
            throw new InvalidOperationException("La sucursal seleccionada no es válida.");
        var asignacion = await context.EContaxUsuariosContexto.SingleAsync(x => x.IdUsuario == userId);
        asignacion.IdSucursal = sucursalId;
        asignacion.FechaActualizacion = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public static bool EsRolJefeEmpresa(string? rol)
    {
        var normalizado = NormalizarRol(rol);
        return normalizado is RolJefeEmpresa ||
            normalizado.Contains("JEFE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool EsRolSucursal(string? rol) =>
        NormalizarRol(rol) is RolAdminSucursal or RolUsuarioSucursal ||
        NormalizarRol(rol).Contains("SUCURSAL", StringComparison.OrdinalIgnoreCase);

    public static string NormalizarRol(string? rol)
    {
        var limpio = (rol ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Replace('Á', 'A')
            .Replace('É', 'E')
            .Replace('Í', 'I')
            .Replace('Ó', 'O')
            .Replace('Ú', 'U')
            .Replace('Ñ', 'N')
            .Replace(' ', '_')
            .Replace('-', '_');

        return limpio switch
        {
            "JEFE" or "ADMINISTRADOR" or "ADMIN_EMPRESA" or "ADMINISTRADOR_EMPRESA" or "SUPERADMIN" or "SUPER_ADMIN" => RolJefeEmpresa,
            "ADMIN" or "ADMINISTRADOR_SUCURSAL" => RolAdminSucursal,
            "USUARIO" => RolUsuarioSucursal,
            _ => limpio
        };
    }

    public static EContaxUserScopeDto ToScopeDto(EContaxUserContext context) => new()
    {
        IdUsuario = context.IdUsuario,
        IdUsuarioTitular = context.IdUsuarioTitular,
        IdEmpresa = context.IdEmpresa,
        IdSucursal = context.IdSucursal,
        Rol = context.Rol,
        EsJefeEmpresa = context.EsJefeEmpresa,
        PuedeFiltrarSucursal = context.EsJefeEmpresa
    };

    private static async Task<string> GetRolUsuarioAsync(AppDbContext context, int userId, int ownerId)
    {
        var rol = await context.Database
            .SqlQuery<string>($"""
                SELECT TOP (1) r.[nombre_rol] AS [Value]
                FROM [dbo].[ECONTAX_USUARIO_ROL] ur
                INNER JOIN [dbo].[rol] r ON r.[id_rol] = ur.[id_rol]
                WHERE ur.[id_usuario] = {userId}
                  AND ISNULL(ur.[estado], 0) = 1
                  AND ISNULL(r.[estado_rol], 0) = 1
                ORDER BY ur.[id_rol]
                """)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrWhiteSpace(rol))
            return NormalizarRol(rol);

        return userId == ownerId ? RolJefeEmpresa : RolUsuarioSucursal;
    }

    private static async Task<int> ResolveEmpresaFallbackAsync(AppDbContext context, int ownerId)
    {
        var fromEmisor = await context.Emisores
            .AsNoTracking()
            .Where(x => x.IdUsuario == ownerId && x.Estado && x.IdEmpresa.HasValue)
            .Select(x => x.IdEmpresa!.Value)
            .FirstOrDefaultAsync();

        if (fromEmisor > 0)
            return fromEmisor;

        var userIds = await GetCuentaUserIdsAsync(context, ownerId);
        var fromCaja = await context.Caja
            .AsNoTracking()
            .Where(x => x.Estado == true && x.IdUsuario.HasValue && userIds.Contains(x.IdUsuario.Value) && x.IdEmpresa.HasValue)
            .Select(x => x.IdEmpresa!.Value)
            .FirstOrDefaultAsync();

        if (fromCaja > 0)
            return fromCaja;

        return ownerId;
    }

    private static async Task<int?> ResolveSucursalFallbackAsync(AppDbContext context, int userId, int ownerId, int empresaId)
    {
        var fromCajaUsuario = await context.Caja
            .AsNoTracking()
            .Where(x =>
                x.Estado == true &&
                x.IdUsuario == userId &&
                x.IdEmpresa == empresaId &&
                x.IdSucursal.HasValue)
            .Select(x => x.IdSucursal!.Value)
            .FirstOrDefaultAsync();

        if (fromCajaUsuario > 0)
            return fromCajaUsuario;

        var userIds = await GetCuentaUserIdsAsync(context, ownerId);
        var fromCajaCuenta = await context.Caja
            .AsNoTracking()
            .Where(x =>
                x.Estado == true &&
                x.IdUsuario.HasValue &&
                userIds.Contains(x.IdUsuario.Value) &&
                x.IdEmpresa == empresaId &&
                x.IdSucursal.HasValue)
            .Select(x => x.IdSucursal!.Value)
            .FirstOrDefaultAsync();

        if (fromCajaCuenta > 0)
            return fromCajaCuenta;

        var fromSucursal = await context.EContaxSucursales
            .AsNoTracking()
            .Where(x => x.IdEmpresa == empresaId && x.Estado)
            .OrderBy(x => x.IdSucursal)
            .Select(x => x.IdSucursal)
            .FirstOrDefaultAsync();

        return fromSucursal > 0 ? fromSucursal : null;
    }

    private static async Task<List<int>> GetCuentaUserIdsAsync(AppDbContext context, int ownerId) =>
        await context.Usuarios
            .AsNoTracking()
            .Where(u => u.IdUsuario == ownerId || (u.idJefe == ownerId && u.estadoAsociado == true))
            .Select(u => u.IdUsuario)
            .ToListAsync();
}

public sealed record EContaxUserContext(
    int IdUsuario,
    int IdUsuarioTitular,
    int IdEmpresa,
    int? IdSucursal,
    string Rol,
    bool EsJefeEmpresa);
