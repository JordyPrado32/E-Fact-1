namespace Simetric.Services;

public sealed class ConnectedUsersService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, (int UserId, bool Connected)> _sessions = new();

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

    public void Register(string sessionId, int userId)
    {
        if (userId <= 0)
        {
            return;
        }

        var changed = false;
        lock (_sync)
        {
            var exists = _sessions.TryGetValue(sessionId, out var current);
            var connected = !exists || current.Connected;
            var session = (userId, connected);
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
                _sessions[sessionId] = (session.UserId, connected);
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
}
