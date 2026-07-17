using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Simetric.Services;

public sealed class AuditActorResolver
{
    private readonly CurrentUserContext _currentUserContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditActorResolver(
        CurrentUserContext currentUserContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _currentUserContext = currentUserContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public int? ResolveCurrentUserId()
    {
        if (_currentUserContext.UserId.HasValue)
        {
            return _currentUserContext.UserId.Value;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var rawUserId =
            httpContext?.User.FindFirst("IdUsuario")?.Value ??
            httpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (int.TryParse(rawUserId, out var claimUserId))
        {
            return claimUserId;
        }

        return httpContext?.Session.GetInt32("Session.IdUsuario");
    }

    public string? ResolveRequestPath()
    {
        return _httpContextAccessor.HttpContext?.Request.Path.Value;
    }

    public string? ResolveRemoteIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }
}
