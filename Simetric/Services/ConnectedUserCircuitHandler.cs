using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Simetric.Services;

public sealed class ConnectedUserCircuitHandler : CircuitHandler
{
    private readonly ConnectedUsersService _connectedUsers;

    public ConnectedUserCircuitHandler(ConnectedUsersService connectedUsers)
    {
        _connectedUsers = connectedUsers;
    }

    public string SessionId { get; } = Guid.NewGuid().ToString("N");

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
