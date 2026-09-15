using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Simetric.Controllers;

[Authorize]
public abstract class UsuarioApiControllerBase : ControllerBase
{
    protected int ResolverIdUsuario(int? idUsuarioSolicitud)
    {
        var claim = User.FindFirst("IdUsuario")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out var idUsuarioClaim) && idUsuarioClaim > 0
            ? idUsuarioClaim
            : idUsuarioSolicitud.GetValueOrDefault();
    }
}
