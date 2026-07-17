using Microsoft.EntityFrameworkCore;
using System.Data;
using Simetric.Data;
using Simetric.Models;
using Simetric.Models.EContax;

namespace Simetric.Services.EContax;

public class EContaxAdministracionService
{
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public EContaxAdministracionService(AppDbContext context, IDbContextFactory<AppDbContext> dbFactory)
    {
        _context = context;
        _dbFactory = dbFactory;
    }

    public async Task<List<EContaxRol>> GetEContaxRolesAsync(bool includeInactivos = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var query = context.EContaxRoles
            .AsNoTracking();

        if (!includeInactivos)
        {
            query = query.Where(item => item.EstadoRol == 1);
        }

        var roles = await query.ToListAsync();

        return roles
            .OrderByDescending(item => item.EstadoRol == 1)
            .ThenBy(item => item.NombreRol ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.IdRol)
            .ToList();
    }

    public async Task<EContaxRol?> GetEContaxRolAsync(int id)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.EContaxRoles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.IdRol == id);
    }

    public async Task<bool> GuardarEContaxRolAsync(EContaxRol modelo)
    {
        var nombre = (modelo.NombreRol ?? string.Empty).Trim();

        if (nombre.Length is < 3 or > 200)
        {
            return false;
        }

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var nombreNormalizado = nombre.ToUpperInvariant();
                var existeNombre = await context.EContaxRoles.AnyAsync(item =>
                    item.IdRol != modelo.IdRol &&
                    item.NombreRol != null &&
                    item.NombreRol.Trim().ToUpper() == nombreNormalizado);

                if (existeNombre)
                {
                    return false;
                }

                if (modelo.IdRol == 0)
                {
                    var siguienteId = (await context.EContaxRoles.MaxAsync(item => (int?)item.IdRol) ?? 0) + 1;

                    context.EContaxRoles.Add(new EContaxRol
                    {
                        IdRol = siguienteId,
                        NombreRol = nombre,
                        PermisoRol = modelo.PermisoRol,
                        EstadoRol = 1
                    });
                }
                else
                {
                    var actual = await context.EContaxRoles.FirstOrDefaultAsync(item => item.IdRol == modelo.IdRol);

                    if (actual is null)
                    {
                        return false;
                    }

                    actual.NombreRol = nombre;
                    actual.PermisoRol = modelo.PermisoRol;
                    actual.EstadoRol = modelo.EstadoRol == 0 ? 0 : 1;
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DesactivarEContaxRolAsync(int id)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        try
        {
            var item = await context.EContaxRoles.FirstOrDefaultAsync(rol => rol.IdRol == id);

            if (item is null)
            {
                return false;
            }

            item.EstadoRol = 0;
            return await context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ReactivarEContaxRolAsync(int id)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        try
        {
            var item = await context.EContaxRoles.FirstOrDefaultAsync(rol => rol.IdRol == id);

            if (item is null)
            {
                return false;
            }

            item.EstadoRol = 1;
            return await context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<EContaxMenu>> GetEContaxMenusAsync(bool includeInactivos = false)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var query = context.EContaxMenus
            .AsNoTracking();

        if (!includeInactivos)
        {
            query = query.Where(item => item.EstadoMenu == 1);
        }

        var menus = await query.ToListAsync();

        return menus
            .OrderBy(item => item.IdPadre ?? 0)
            .ThenBy(item => item.NombreMenu ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.IdMenu)
            .ToList();
    }

    public async Task<List<EContaxMenu>> GetEContaxMenusByRolIdAsync(int idRol)
    {
        if (idRol <= 0)
        {
            return new List<EContaxMenu>();
        }

        await using var context = await _dbFactory.CreateDbContextAsync();

        var rolValido = await context.EContaxRoles
            .AsNoTracking()
            .AnyAsync(item => item.IdRol == idRol);

        if (!rolValido)
        {
            return new List<EContaxMenu>();
        }

        return await context.EContaxMenus
            .FromSqlInterpolated($@"
                SELECT m.*
                FROM [dbo].[menu] m
                INNER JOIN [dbo].[ECONTAX_ROL_MENU] rm ON rm.[id_menu] = m.[id_menu]
                WHERE rm.[id_rol] = {idRol}
                  AND ISNULL(m.[estado_menu], 0) = 1")
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<int?> GetEContaxRolIdUsuarioAsync(int idUsuario)
    {
        if (idUsuario <= 0)
        {
            return null;
        }

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            var idRol = await context.Database
                .SqlQuery<int>($@"
                    SELECT TOP (1) ur.[id_rol] AS [Value]
                    FROM [dbo].[ECONTAX_USUARIO_ROL] ur
                    INNER JOIN [dbo].[rol] r ON r.[id_rol] = ur.[id_rol]
                    WHERE ur.[id_usuario] = {idUsuario}
                      AND ISNULL(ur.[estado], 0) = 1
                      AND ISNULL(r.[estado_rol], 0) = 1
                    ORDER BY ur.[id_rol]")
                .FirstOrDefaultAsync();

            return idRol > 0 ? idRol : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<EContaxUsuarioRolAsignadoDto>> GetEContaxUsuariosRolesAsync()
    {
        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();
            return await context.Database
                .SqlQuery<EContaxUsuarioRolAsignadoDto>($@"
                    SELECT
                        ur.[id_usuario] AS [IdUsuario],
                        ur.[id_rol] AS [IdRol],
                        r.[nombre_rol] AS [NombreRol],
                        CAST(ISNULL(ur.[estado], 0) AS bit) AS [Estado],
                        ur.[fecha_creacion] AS [FechaCreacion],
                        ur.[fecha_actualizacion] AS [FechaActualizacion]
                    FROM [dbo].[ECONTAX_USUARIO_ROL] ur
                    INNER JOIN [dbo].[rol] r ON r.[id_rol] = ur.[id_rol]")
                .ToListAsync();
        }
        catch
        {
            return new List<EContaxUsuarioRolAsignadoDto>();
        }
    }

    public async Task<List<EContaxMenu>> GetEContaxMenusVisiblesPorUsuarioAsync(int idUsuario)
    {
        if (idUsuario <= 0)
        {
            return new List<EContaxMenu>();
        }

        try
        {
            await using var context = await _dbFactory.CreateDbContextAsync();

            var menus = await context.EContaxMenus
                .FromSqlInterpolated($@"
                    SELECT DISTINCT m.*
                    FROM [dbo].[menu] m
                    INNER JOIN [dbo].[ECONTAX_ROL_MENU] rm ON rm.[id_menu] = m.[id_menu]
                    INNER JOIN [dbo].[rol] r ON r.[id_rol] = rm.[id_rol]
                    INNER JOIN [dbo].[ECONTAX_USUARIO_ROL] ur ON ur.[id_rol] = r.[id_rol]
                    WHERE ur.[id_usuario] = {idUsuario}
                      AND ISNULL(ur.[estado], 0) = 1
                      AND ISNULL(r.[estado_rol], 0) = 1
                      AND ISNULL(m.[estado_menu], 0) = 1")
                .AsNoTracking()
                .ToListAsync();

            return await CompletarPadresMenuAsync(context, menus);
        }
        catch
        {
            return new List<EContaxMenu>();
        }
    }

    private static async Task<List<EContaxMenu>> CompletarPadresMenuAsync(
        AppDbContext context,
        List<EContaxMenu> menus)
    {
        var idsExistentes = menus.Select(item => item.IdMenu).ToHashSet();
        var idsPadresFaltantes = menus
            .Where(item => item.IdPadre is > 0 && !idsExistentes.Contains(item.IdPadre.Value))
            .Select(item => item.IdPadre!.Value)
            .Distinct()
            .ToList();

        if (idsPadresFaltantes.Any())
        {
            var padres = await context.EContaxMenus
                .AsNoTracking()
                .Where(item =>
                    idsPadresFaltantes.Contains(item.IdMenu) &&
                    item.EstadoMenu == 1)
                .ToListAsync();

            menus.AddRange(padres.Where(item => idsExistentes.Add(item.IdMenu)));
        }

        return menus
            .OrderBy(item => item.IdPadre ?? 0)
            .ThenBy(item => item.IdMenu)
            .ToList();
    }

    public async Task<bool> AsignarEContaxMenusARolAsync(int idRol, List<int> idMenus)
    {
        if (idRol <= 0)
        {
            return false;
        }

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var rolValido = await context.EContaxRoles.AnyAsync(item =>
                    item.IdRol == idRol &&
                    item.EstadoRol == 1);

                if (!rolValido)
                {
                    return false;
                }

                var idsValidos = await context.EContaxMenus
                    .AsNoTracking()
                    .Where(item =>
                        item.EstadoMenu == 1 &&
                        idMenus.Contains(item.IdMenu))
                    .Select(item => item.IdMenu)
                    .ToListAsync();

                await context.Database.ExecuteSqlInterpolatedAsync($@"
                    DELETE FROM [dbo].[ECONTAX_ROL_MENU]
                    WHERE [id_rol] = {idRol}");

                foreach (var idMenu in idsValidos.Distinct())
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO [dbo].[ECONTAX_ROL_MENU] ([id_rol], [id_menu])
                        VALUES ({idRol}, {idMenu})");
                }

                await transaction.CommitAsync();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AsignarEContaxRolAUsuarioAsync(int idUsuario, int idRol)
    {
        if (idUsuario <= 0 || idRol <= 0)
        {
            return false;
        }

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var rolValido = await context.EContaxRoles.AnyAsync(item =>
                    item.IdRol == idRol &&
                    item.EstadoRol == 1);

                if (!rolValido)
                {
                    return false;
                }

                var actualizados = await context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE [dbo].[ECONTAX_USUARIO_ROL]
                    SET [id_rol] = {idRol},
                        [estado] = 1,
                        [fecha_actualizacion] = SYSUTCDATETIME()
                    WHERE [id_usuario] = {idUsuario}");

                if (actualizados == 0)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO [dbo].[ECONTAX_USUARIO_ROL] ([id_usuario], [id_rol], [estado])
                        VALUES ({idUsuario}, {idRol}, 1)");
                }

                await transaction.CommitAsync();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GuardarEContaxMenuAsync(EContaxMenu modelo)
    {
        var nombre = (modelo.NombreMenu ?? string.Empty).Trim();
        var ruta = (modelo.UrlMenu ?? string.Empty).Trim();
        string? descripcion = null;
        var icono = string.IsNullOrWhiteSpace(modelo.IconoMenu) ? null : modelo.IconoMenu.Trim();
        var idPadre = modelo.IdPadre is null or 0 ? null : modelo.IdPadre;

        if (nombre.Length is < 3 or > 200 ||
            idPadre == modelo.IdMenu ||
            ruta.Length > 200 ||
            icono?.Length > 50)
        {
            return false;
        }

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                if (idPadre.HasValue)
                {
                    var padreValido = await context.EContaxMenus.AnyAsync(item =>
                        item.IdMenu == idPadre.Value &&
                        item.EstadoMenu == 1);

                    if (!padreValido)
                    {
                        return false;
                    }
                }

                var existeNombre = await context.EContaxMenus.AnyAsync(item =>
                    item.IdMenu != modelo.IdMenu &&
                    item.IdPadre == idPadre &&
                    item.NombreMenu != null &&
                    item.NombreMenu.Trim().ToUpper() == nombre.ToUpperInvariant());

                if (existeNombre)
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(ruta))
                {
                    var existeRuta = await context.EContaxMenus.AnyAsync(item =>
                        item.IdMenu != modelo.IdMenu &&
                        item.UrlMenu != null &&
                        item.UrlMenu.Trim().ToUpper() == ruta.ToUpperInvariant());

                    if (existeRuta)
                    {
                        return false;
                    }
                }

                if (modelo.IdMenu == 0)
                {
                    var siguienteId = (await context.EContaxMenus.MaxAsync(item => (int?)item.IdMenu) ?? 0) + 1;

                    context.EContaxMenus.Add(new EContaxMenu
                    {
                        IdMenu = siguienteId,
                        NombreMenu = nombre,
                        PerteneceMenu = modelo.PerteneceMenu,
                        UrlMenu = ruta,
                        DescripcionMenu = descripcion,
                        IconoMenu = icono,
                        EstadoMenu = 1,
                        IdPadre = idPadre
                    });
                }
                else
                {
                    var actual = await context.EContaxMenus.FirstOrDefaultAsync(item => item.IdMenu == modelo.IdMenu);

                    if (actual is null)
                    {
                        return false;
                    }

                    actual.NombreMenu = nombre;
                    actual.UrlMenu = ruta;
                    actual.DescripcionMenu = descripcion;
                    actual.IconoMenu = icono;
                    actual.EstadoMenu = modelo.EstadoMenu == 0 ? 0 : 1;
                    actual.IdPadre = idPadre;
                    actual.PerteneceMenu = modelo.PerteneceMenu;
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ActualizarOrdenMenusAsync(List<EContaxMenu> menus)
    {
        if (menus == null || !menus.Any())
        {
            return false;
        }

        await using var strategyContext = await _dbFactory.CreateDbContextAsync();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var context = await _dbFactory.CreateDbContextAsync();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                foreach (var menu in menus)
                {
                    var actual = await context.EContaxMenus.FirstOrDefaultAsync(item => item.IdMenu == menu.IdMenu);
                    if (actual is not null)
                    {
                        actual.OrdenMenu = menu.OrdenMenu;
                    }
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EliminarLogicoEContaxMenuAsync(int idMenu)

    {
        if (idMenu <= 0)
        {
            return false;
        }

        await using var context = await _dbFactory.CreateDbContextAsync();

        try
        {
            var menus = await context.EContaxMenus
                .Where(item =>
                    (item.IdMenu == idMenu || item.IdPadre == idMenu))
                .ToListAsync();

            if (!menus.Any())
            {
                return false;
            }

            foreach (var menu in menus)
            {
                menu.EstadoMenu = 0;
            }

            await context.SaveChangesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<FormasPago>> GetFormasPagoActivasAsync()
    {
        var items = await _context.FormasPago
            .AsNoTracking()
            .Where(item => item.Estado == true)
            .ToListAsync();

        return items
            .OrderBy(item => ParseCodigoNumerico(item.Codigo))
            .ThenBy(item => item.Codigo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Descripcion ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<FormasPago?> GetFormaPagoAsync(int id) =>
        await _context.FormasPago
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);

    public async Task<bool> GuardarFormaPagoAsync(FormasPago modelo)
    {
        try
        {
            var codigo = (modelo.Codigo ?? string.Empty).Trim();
            var descripcion = (modelo.Descripcion ?? string.Empty).Trim();
            var descripcionSri = (modelo.DescripcionSri ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(codigo) ||
                string.IsNullOrWhiteSpace(descripcion) ||
                string.IsNullOrWhiteSpace(descripcionSri) ||
                !codigo.All(char.IsDigit) ||
                codigo.Length > 2 ||
                (modelo.TipoVenta != true && modelo.TipoCompra != true))
            {
                return false;
            }

            var descripcionNormalizada = descripcion.ToUpperInvariant();

            var existeCodigo = await _context.FormasPago.AnyAsync(item =>
                item.Estado == true &&
                item.Id != modelo.Id &&
                item.Codigo == codigo);

            if (existeCodigo)
            {
                return false;
            }

            var existeDescripcion = await _context.FormasPago.AnyAsync(item =>
                item.Estado == true &&
                item.Id != modelo.Id &&
                item.Descripcion != null &&
                item.Descripcion.ToUpper() == descripcionNormalizada);

            if (existeDescripcion)
            {
                return false;
            }

            if (modelo.Id == 0)
            {
                _context.FormasPago.Add(new FormasPago
                {
                    Codigo = codigo,
                    Descripcion = descripcion,
                    DescripcionSri = descripcionSri,
                    TipoVenta = modelo.TipoVenta == true,
                    TipoCompra = modelo.TipoCompra == true,
                    Estado = true
                });
            }
            else
            {
                var actual = await _context.FormasPago.FirstOrDefaultAsync(item => item.Id == modelo.Id);
                if (actual is null)
                {
                    return false;
                }

                actual.Codigo = codigo;
                actual.Descripcion = descripcion;
                actual.DescripcionSri = descripcionSri;
                actual.TipoVenta = modelo.TipoVenta == true;
                actual.TipoCompra = modelo.TipoCompra == true;
                actual.Estado = true;
            }

            return await _context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DesactivarFormaPagoAsync(int id)
    {
        try
        {
            var item = await _context.FormasPago.FindAsync(id);
            if (item is null)
            {
                return false;
            }

            item.Estado = false;
            return await _context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Identificacion>> GetIdentificacionesActivasAsync()
    {
        var items = await _context.Identificacion
            .AsNoTracking()
            .Where(item => item.Estado == true)
            .ToListAsync();

        return items
            .OrderBy(item => ParseCodigoNumerico(item.IdeCodigo))
            .ThenBy(item => item.IdeCodigo ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.IdeDescripcion ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Identificacion?> GetIdentificacionAsync(int id) =>
        await _context.Identificacion
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.IdeSec == id);

    public async Task<bool> GuardarIdentificacionAsync(Identificacion modelo)
    {
        try
        {
            var codigo = (modelo.IdeCodigo ?? string.Empty).Trim();
            var descripcion = (modelo.IdeDescripcion ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(codigo) ||
                string.IsNullOrWhiteSpace(descripcion) ||
                !codigo.All(char.IsDigit) ||
                codigo.Length > 10 ||
                descripcion.Length > 80)
            {
                return false;
            }

            var descripcionNormalizada = descripcion.ToUpperInvariant();

            var existeCodigo = await _context.Identificacion.AnyAsync(item =>
                item.IdeSec != modelo.IdeSec &&
                item.IdeCodigo == codigo);

            if (existeCodigo)
            {
                return false;
            }

            var existeDescripcion = await _context.Identificacion.AnyAsync(item =>
                item.IdeSec != modelo.IdeSec &&
                item.IdeDescripcion != null &&
                item.IdeDescripcion.ToUpper() == descripcionNormalizada);

            if (existeDescripcion)
            {
                return false;
            }

            if (modelo.IdeSec == 0)
            {
                _context.Identificacion.Add(new Identificacion
                {
                    IdeCodigo = codigo,
                    IdeDescripcion = descripcion,
                    Estado = true
                });
            }
            else
            {
                var actual = await _context.Identificacion.FirstOrDefaultAsync(item => item.IdeSec == modelo.IdeSec);
                if (actual is null)
                {
                    return false;
                }

                actual.IdeCodigo = codigo;
                actual.IdeDescripcion = descripcion;
                actual.Estado = true;
            }

            return await _context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DesactivarIdentificacionAsync(int id)
    {
        try
        {
            var item = await _context.Identificacion.FindAsync(id);
            if (item is null)
            {
                return false;
            }

            item.Estado = false;
            return await _context.SaveChangesAsync() > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Codigosimpuesto>> GetCodigosImpuestosAsync(string searchText = "", int skip = 0, int take = int.MaxValue, bool includeInactivos = false)
    {
        var query = _context.Codigoimpuestos.AsNoTracking().AsQueryable();

        if (!includeInactivos)
            query = query.Where(item => item.Estado == "A");

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(item =>
                item.Codigo.Contains(searchText) ||
                (item.Descripcion ?? string.Empty).Contains(searchText));
        }

        return await query
            .OrderBy(item => item.Codigo)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<bool> ExisteCodigoImpuestoAsync(string codigo)
    {
        codigo = (codigo ?? string.Empty).Trim().ToUpperInvariant();

        return await _context.Codigoimpuestos
            .AsNoTracking()
            .AnyAsync(item => item.Codigo.ToUpper() == codigo);
    }

    public async Task SaveCodigoAsync(Codigosimpuesto modelo)
    {
        modelo.Codigo = (modelo.Codigo ?? string.Empty).Trim().ToUpperInvariant();
        modelo.Descripcion = (modelo.Descripcion ?? string.Empty).Trim();
        modelo.Estado = "A";

        _context.Codigoimpuestos.Add(modelo);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateCodigoAsync(Codigosimpuesto modelo)
    {
        var codigo = (modelo.Codigo ?? string.Empty).Trim().ToUpperInvariant();
        var actual = await _context.Codigoimpuestos.FirstOrDefaultAsync(item => item.Codigo == codigo);

        if (actual is null)
            return;

        actual.Descripcion = (modelo.Descripcion ?? string.Empty).Trim();
        actual.Estado = "A";
        await _context.SaveChangesAsync();
    }

    public async Task SoftDeleteCodigoImpuestoAsync(string codigo)
    {
        codigo = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        var item = await _context.Codigoimpuestos.FirstOrDefaultAsync(x => x.Codigo == codigo);

        if (item is null)
            return;

        item.Estado = "I";
        await _context.SaveChangesAsync();
    }

    public async Task<List<Porcentajeiva>> GetPorcentajesIvaAsync(string searchText = "", int skip = 0, int take = int.MaxValue, bool includeInactivos = false)
    {
        var query = _context.Porcentajeivas.AsNoTracking().AsQueryable();

        if (!includeInactivos)
            query = query.Where(item => item.Estado == "A");

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(item =>
                item.Codigo.Contains(searchText) ||
                (item.Descripcion ?? string.Empty).Contains(searchText) ||
                (item.Valor ?? string.Empty).Contains(searchText));
        }

        return await query
            .OrderBy(item => item.ValorCalculo)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<bool> ExistePorcentajeIvaAsync(string codigo)
    {
        codigo = (codigo ?? string.Empty).Trim();

        return await _context.Porcentajeivas
            .AsNoTracking()
            .AnyAsync(item => item.Codigo == codigo);
    }

    public async Task SaveIvaAsync(Porcentajeiva modelo)
    {
        modelo.Codigo = (modelo.Codigo ?? string.Empty).Trim();
        modelo.Descripcion = (modelo.Descripcion ?? string.Empty).Trim();
        modelo.Valor = (modelo.Valor ?? string.Empty).Trim();
        modelo.Estado = "A";

        _context.Porcentajeivas.Add(modelo);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateIvaAsync(Porcentajeiva modelo)
    {
        var codigo = (modelo.Codigo ?? string.Empty).Trim();
        var actual = await _context.Porcentajeivas.FirstOrDefaultAsync(item => item.Codigo == codigo);

        if (actual is null)
            return;

        actual.Descripcion = (modelo.Descripcion ?? string.Empty).Trim();
        actual.Valor = (modelo.Valor ?? string.Empty).Trim();
        actual.ValorCalculo = modelo.ValorCalculo;
        actual.Estado = "A";
        await _context.SaveChangesAsync();
    }

    public async Task SoftDeletePorcentajeIvaAsync(string codigo)
    {
        codigo = (codigo ?? string.Empty).Trim();
        var item = await _context.Porcentajeivas.FirstOrDefaultAsync(x => x.Codigo == codigo);

        if (item is null)
            return;

        item.Estado = "I";
        await _context.SaveChangesAsync();
    }

    public async Task<List<RetencionIva>> GetRetencionesIvaAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.RetencionIva
            .AsNoTracking()
            .OrderBy(item => item.Codigo)
            .ToListAsync();
    }

    public async Task GuardarRetencionIvaAsync(RetencionIva modelo, bool isEdit)
    {
        ValidarRetencionNumero(modelo.Codigo, modelo.Descripcion);
        await using var context = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            var existe = await context.RetencionIva.AnyAsync(item => item.Codigo == modelo.Codigo);
            if (existe)
            {
                throw new InvalidOperationException("Ese codigo ya existe.");
            }

            context.RetencionIva.Add(modelo);
        }
        else
        {
            context.RetencionIva.Update(modelo);
        }

        await context.SaveChangesAsync();
    }

    public async Task EliminarRetencionIvaAsync(int codigo)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var item = await context.RetencionIva.FindAsync(codigo);
        if (item is null)
        {
            return;
        }

        context.RetencionIva.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task<List<RetencionIsd>> GetRetencionesIsdAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.RetencionIsd
            .AsNoTracking()
            .OrderBy(item => item.Codigo)
            .ToListAsync();
    }

    public async Task GuardarRetencionIsdAsync(RetencionIsd modelo, bool isEdit)
    {
        ValidarRetencionNumero(modelo.Codigo, modelo.Descripcion);
        await using var context = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            var existe = await context.RetencionIsd.AnyAsync(item => item.Codigo == modelo.Codigo);
            if (existe)
            {
                throw new InvalidOperationException("Ese codigo ya existe.");
            }

            context.RetencionIsd.Add(modelo);
        }
        else
        {
            context.RetencionIsd.Update(modelo);
        }

        await context.SaveChangesAsync();
    }

    public async Task EliminarRetencionIsdAsync(int codigo)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var item = await context.RetencionIsd.FindAsync(codigo);
        if (item is null)
        {
            return;
        }

        context.RetencionIsd.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task<List<RetencionRenta>> GetRetencionesRentaAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.RetencionRenta
            .AsNoTracking()
            .OrderBy(item => item.Codigo)
            .ToListAsync();
    }

    public async Task GuardarRetencionRentaAsync(RetencionRenta modelo, bool isEdit)
    {
        ValidarRetencionTexto(modelo.Codigo, modelo.Descripcion);
        modelo.Estado ??= true;
        await using var context = await _dbFactory.CreateDbContextAsync();

        if (!isEdit)
        {
            var existe = await context.RetencionRenta.AnyAsync(item => item.Codigo == modelo.Codigo);
            if (existe)
            {
                throw new InvalidOperationException("Ese codigo ya existe.");
            }

            context.RetencionRenta.Add(modelo);
        }
        else
        {
            context.RetencionRenta.Update(modelo);
        }

        await context.SaveChangesAsync();
    }

    public async Task EliminarRetencionRentaAsync(string codigo)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var item = await context.RetencionRenta.FindAsync(codigo);
        if (item is null)
        {
            return;
        }

        context.RetencionRenta.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task<List<Auditoria>> GetAuditoriasSqlAsync()
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        return await context.Auditorias
            .AsNoTracking()
            .Include(item => item.Usuario)
            .OrderByDescending(item => item.Fecha)
            .ThenByDescending(item => item.IdAuditoria)
            .ToListAsync();
    }

    public async Task<(List<LogIniciosSesion> Items, int Total)> GetLogsInicioAsync(DateTime? fechaDesde, DateTime? fechaHasta, int page, int pageSize)
    {
        await PurgarLogsFueraDeMesActualAsync(DateTime.Now);

        var query = _context.LogIniciosSesiones
            .AsNoTracking()
            .Include(item => item.IdUsuarioNavigation)
            .AsQueryable();

        if (fechaDesde.HasValue)
            query = query.Where(item => item.FechaAcceso >= fechaDesde.Value.Date);

        if (fechaHasta.HasValue)
            query = query.Where(item => item.FechaAcceso <= fechaHasta.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Max(pageSize, 1);

        var items = await query
            .OrderByDescending(item => item.FechaAcceso)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> EliminarLogInicioAsync(int id)
    {
        var item = await _context.LogIniciosSesiones.FindAsync(id);

        if (item is null)
            return false;

        _context.LogIniciosSesiones.Remove(item);
        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<int> EliminarTodosLogsInicioAsync() =>
        await _context.LogIniciosSesiones.ExecuteDeleteAsync();

    public async Task<int> EliminarLogsInicioPeriodoAsync(DateTime inicio, DateTime fin) =>
        await _context.LogIniciosSesiones
            .Where(item => item.FechaAcceso >= inicio && item.FechaAcceso < fin)
            .ExecuteDeleteAsync();

    private async Task<int> PurgarLogsFueraDeMesActualAsync(DateTime fechaReferencia)
    {
        var inicioMes = new DateTime(fechaReferencia.Year, fechaReferencia.Month, 1);
        var inicioMesSiguiente = inicioMes.AddMonths(1);

        return await _context.LogIniciosSesiones
            .Where(item => item.FechaAcceso < inicioMes || item.FechaAcceso >= inicioMesSiguiente)
            .ExecuteDeleteAsync();
    }

    private static void ValidarRetencionNumero(int codigo, string? descripcion)
    {
        if (codigo <= 0)
        {
            throw new InvalidOperationException("El codigo es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            throw new InvalidOperationException("La descripcion es obligatoria.");
        }
    }

    private static void ValidarRetencionTexto(string? codigo, string? descripcion)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new InvalidOperationException("El codigo es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            throw new InvalidOperationException("La descripcion es obligatoria.");
        }
    }

    private static int ParseCodigoNumerico(string? codigo) =>
        int.TryParse(codigo?.Trim(), out var numero) ? numero : int.MaxValue;
}

public sealed class EContaxUsuarioRolAsignadoDto
{
    public int IdUsuario { get; set; }
    public int IdRol { get; set; }
    public string? NombreRol { get; set; }
    public bool Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaActualizacion { get; set; }
}
