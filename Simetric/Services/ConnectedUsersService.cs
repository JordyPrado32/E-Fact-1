namespace Simetric.Services;

public sealed class ConnectedUsersService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ConnectedUserSession> _sessions = new();

    public event Action? Changed;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _sessions.Values
                    .Where(session => session.Connected)
                    .Select(session => session.UserId)
                    .Distinct()
                    .Count();
            }
        }
    }

    public IReadOnlyList<ConnectedUserSession> GetActiveSessions()
    {
        lock (_sync)
        {
            return _sessions.Values
                .Where(session => session.Connected)
                .OrderByDescending(session => session.LastSeenUtc)
                .ToList();
        }
    }

    public void Register(string sessionId, int userId, string? ipAddress, string? userAgent, string service)
    {
        if (userId <= 0)
        {
            return;
        }

        var changed = false;
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var exists = _sessions.TryGetValue(sessionId, out var current);
            var session = new ConnectedUserSession(
                sessionId,
                userId,
                true,
                string.IsNullOrWhiteSpace(ipAddress) ? current?.IpAddress : ipAddress,
                string.IsNullOrWhiteSpace(userAgent) ? current?.UserAgent : userAgent,
                string.IsNullOrWhiteSpace(service) ? current?.Service ?? "E-FACT" : service,
                current?.ConnectedAtUtc ?? now,
                now);
            if (!exists || current != session)
            {
                _sessions[sessionId] = session;
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void SetConnectionState(string sessionId, bool connected)
    {
        var changed = false;
        lock (_sync)
        {
            if (_sessions.TryGetValue(sessionId, out var session) && session.Connected != connected)
            {
                _sessions[sessionId] = session with { Connected = connected, LastSeenUtc = DateTimeOffset.UtcNow };
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Unregister(string sessionId)
    {
        var changed = false;
        lock (_sync)
        {
            changed = _sessions.Remove(sessionId);
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void UpdateService(string sessionId, string service)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            return;
        }

        var changed = false;
        lock (_sync)
        {
            if (_sessions.TryGetValue(sessionId, out var session) &&
                !string.Equals(session.Service, service, StringComparison.Ordinal))
            {
                _sessions[sessionId] = session with { Service = service, LastSeenUtc = DateTimeOffset.UtcNow };
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }
}

public sealed record ConnectedUserSession(
    string SessionId,
    int UserId,
    bool Connected,
    string? IpAddress,
    string? UserAgent,
    string Service,
    DateTimeOffset ConnectedAtUtc,
    DateTimeOffset LastSeenUtc);
