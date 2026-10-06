using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Simetric.Services.EContax;
using Simetric.Models.EContax;

namespace Simetric.Controllers.EContax;

[Authorize]
public abstract class EContaxApiControllerBase : Controller
{
    protected abstract string RutaPermiso { get; }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var claim = User.FindFirst("IdUsuario")?.Value ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var userId) || userId <= 0)
        {
            context.Result = Unauthorized();
            return;
        }
        var seguridad = HttpContext.RequestServices.GetRequiredService<EContaxSeguridadService>();
        var accion = HttpContext.Request.Method switch
        {
            "POST" => EContaxAccion.Crear,
            "DELETE" => EContaxAccion.Eliminar,
            "PUT" or "PATCH" => HttpContext.Request.Path.Value?.EndsWith("/desactivar", StringComparison.OrdinalIgnoreCase) == true
                ? EContaxAccion.Eliminar : EContaxAccion.Editar,
            _ => EContaxAccion.Ver
        };
        if (!await seguridad.PuedeAccederRutaAsync(userId, RutaPermiso, accion))
        {
            context.Result = Forbid();
            return;
        }
        if (context.ActionArguments.ContainsKey("userId")) context.ActionArguments["userId"] = userId;
        await next();
    }
}
