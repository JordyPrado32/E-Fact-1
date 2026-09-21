using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;

namespace Simetric.Services;

public sealed class ConnectedUserCircuitHandler : CircuitHandler
{
    private readonly ConnectedUsersService _connectedUsers;
    private readonly string? _ipAddress;
    private readonly string? _userAgent;

    public ConnectedUserCircuitHandler(ConnectedUsersService connectedUsers, IHttpContextAccessor httpContextAccessor)
    {
        _connectedUsers = connectedUsers;
        var request = httpContextAccessor.HttpContext?.Request;
        _ipAddress = request?.HttpContext.Connection.RemoteIpAddress?.ToString();
        _userAgent = request?.Headers.UserAgent.ToString();
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string? IpAddress => _ipAddress;
    public string? UserAgent => _userAgent;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectedUsers.SetConnectionState(SessionId, true);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectedUsers.SetConnectionState(SessionId, false);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connectedUsers.Unregister(SessionId);
        return Task.CompletedTask;
    }
}
