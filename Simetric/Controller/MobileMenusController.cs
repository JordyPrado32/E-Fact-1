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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var idTipoUsuario = await db.Usuarios.AsNoTracking()
            .Where(user => user.IdUsuario == userId && user.Estado == true)
            .Select(user => user.IdTipoUsuario)
            .FirstOrDefaultAsync(cancellationToken);
        if (idTipoUsuario is null or <= 0) return Unauthorized();

        var menus = await _menuService.GetMenusByRol(idTipoUsuario.Value);
        return Ok(menus.Select(menu => new
        {
            id = menu.IdMenu,
            idPadre = menu.IdMenuPadre,
            nombre = menu.NombreMenu,
            ruta = menu.RutaMenu,
            icono = menu.IconoMenu,
            orden = menu.OrdenMenu,
            estado = menu.EstadoMenu,
            habilitado = menu.EstadoMenu
        }));
    }
}
