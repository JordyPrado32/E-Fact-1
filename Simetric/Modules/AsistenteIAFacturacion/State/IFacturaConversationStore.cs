namespace Simetric.Modules.AsistenteIAFacturacion.State;

public interface IFacturaConversationStore
{
    Task<FacturaConversationState> GetOrCreateAsync(int userId, string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(FacturaConversationState state, CancellationToken cancellationToken = default);
    Task ClearAsync(int userId, string sessionId, CancellationToken cancellationToken = default);
}
