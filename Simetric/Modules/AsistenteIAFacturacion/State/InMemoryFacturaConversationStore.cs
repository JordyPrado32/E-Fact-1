using System.Collections.Concurrent;

namespace Simetric.Modules.AsistenteIAFacturacion.State;

public sealed class InMemoryFacturaConversationStore : IFacturaConversationStore
{
    private readonly ConcurrentDictionary<string, FacturaConversationState> _store = new();

    public Task<FacturaConversationState> GetOrCreateAsync(int userId, string sessionId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(userId, sessionId);
        var state = _store.GetOrAdd(key, _ => new FacturaConversationState
        {
            UserId = userId,
            SessionId = sessionId
        });

        state.ActualizadoEn = DateTimeOffset.UtcNow;
        return Task.FromResult(state);
    }

    public Task SaveAsync(FacturaConversationState state, CancellationToken cancellationToken = default)
    {
        _store[BuildKey(state.UserId, state.SessionId)] = state;
        state.ActualizadoEn = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task ClearAsync(int userId, string sessionId, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(BuildKey(userId, sessionId), out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(int userId, string sessionId) => $"{userId}:{sessionId}".ToLowerInvariant();
}
