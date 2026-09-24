using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Services;

namespace Simetric.Controllers;

[Authorize]
[ApiController]
[Route("api/mobile/menus")]
public sealed class MobileMenusController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IMenuService _menuService;

    public MobileMenusController(IDbContextFactory<AppDbContext> dbFactory, IMenuService menuService)
    {
        _dbFactory = dbFactory;
        _menuService = menuService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMenus(CancellationToken cancellationToken = default)
    {
        var claim = User.FindFirst("IdUsuario")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(claim, out var userId) || userId <= 0) return Unauthorized();

        await _menuService.EnsureSchemaAsync();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var idTipoUsuario = await db.Usuarios.AsNoTracking()
            .Where(user => user.IdUsuario == userId && user.Estado == true)
            .Select(user => user.IdTipoUsuario)
            .FirstOrDefaultAsync(cancellationToken);
        if (idTipoUsuario is null or <= 0) return Unauthorized();

        var menusRol = await _menuService.GetMenusByRol(idTipoUsuario.Value);
        var catalogo = await _menuService.GetAllMenus();
        var menusPorId = catalogo.ToDictionary(menu => menu.IdMenu);
        var idsVisibles = menusRol.Select(menu => menu.IdMenu).ToHashSet();

        foreach (var menu in menusRol)
        {
            var padreId = menu.IdMenuPadre;
            while (padreId > 0 && idsVisibles.Add(padreId) && menusPorId.TryGetValue(padreId, out var padre))
                padreId = padre.IdMenuPadre;
        }

        var menus = catalogo
            .Where(menu => idsVisibles.Contains(menu.IdMenu))
            .OrderBy(menu => menu.IdMenuPadre)
            .ThenBy(menu => menu.OrdenMenu ?? 9999)
            .ThenBy(menu => menu.IdMenu)
            .ToList();

        return Ok(menus.Select(menu => new
        {
            id = menu.IdMenu,
            idPadre = menu.IdMenuPadre,
            nombre = menu.NombreMenu,
            ruta = menu.RutaMenu,
            icono = menu.IconoMenu,
            orden = menu.OrdenMenu,
            estado = menu.EstadoMenu,
            habilitado = menu.EstadoMenu == true,
            tipo = menus.Any(child => child.IdMenuPadre == menu.IdMenu) ? "grupo" : "vista",
            hijos = menus.Where(child => child.IdMenuPadre == menu.IdMenu).Select(child => child.IdMenu).ToArray()
        }));
    }
}
