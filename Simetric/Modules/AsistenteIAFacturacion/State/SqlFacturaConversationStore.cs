using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Simetric.Data;
using Simetric.Models;
using Simetric.Services;

namespace Simetric.Modules.AsistenteIAFacturacion.State;

public sealed class SqlFacturaConversationStore : IFacturaConversationStore
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FacturaPersistenceSchemaService _schemaService;

    public SqlFacturaConversationStore(
        IDbContextFactory<AppDbContext> dbFactory,
        FacturaPersistenceSchemaService schemaService)
    {
        _dbFactory = dbFactory;
        _schemaService = schemaService;
    }

    public async Task<FacturaConversationState> GetOrCreateAsync(
        int userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await _schemaService.EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var snapshot = await context.FacturaConversationSnapshots
            .SingleOrDefaultAsync(x => x.IdUsuario == userId && x.SessionId == sessionId, cancellationToken);

        if (snapshot is not null && snapshot.ExpiraEn > now)
        {
            var existingState = TryDeserialize(snapshot.EstadoJson, userId, sessionId);
            if (existingState is not null)
                return existingState;
        }

        var state = new FacturaConversationState
        {
            UserId = userId,
            SessionId = sessionId,
            ActualizadoEn = now
        };

        if (snapshot is null)
        {
            context.FacturaConversationSnapshots.Add(ToSnapshot(state, now));
        }
        else
        {
            snapshot.EstadoJson = JsonSerializer.Serialize(state);
            snapshot.EstadoVersion = state.EstadoVersion;
            snapshot.ActualizadoEn = now;
            snapshot.ExpiraEn = now.Add(StateLifetime);
        }

        await context.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task SaveAsync(FacturaConversationState state, CancellationToken cancellationToken = default)
    {
        await _schemaService.EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var snapshot = await context.FacturaConversationSnapshots
            .SingleOrDefaultAsync(x => x.IdUsuario == state.UserId && x.SessionId == state.SessionId, cancellationToken);

        if (snapshot is null)
        {
            context.FacturaConversationSnapshots.Add(ToSnapshot(state, now));
        }
        else
        {
            if (snapshot.EstadoVersion > state.EstadoVersion)
                throw new DbUpdateConcurrencyException("El estado de la conversación fue actualizado por otra solicitud.");

            snapshot.EstadoJson = JsonSerializer.Serialize(state);
            snapshot.EstadoVersion = state.EstadoVersion;
            snapshot.ActualizadoEn = now;
            snapshot.ExpiraEn = now.Add(StateLifetime);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(int userId, string sessionId, CancellationToken cancellationToken = default)
    {
        await _schemaService.EnsureSchemaAsync();
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshot = await context.FacturaConversationSnapshots
            .SingleOrDefaultAsync(x => x.IdUsuario == userId && x.SessionId == sessionId, cancellationToken);
        if (snapshot is null)
            return;

        context.FacturaConversationSnapshots.Remove(snapshot);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static FacturaConversationSnapshot ToSnapshot(FacturaConversationState state, DateTimeOffset now) => new()
    {
        IdUsuario = state.UserId,
        SessionId = state.SessionId,
        EstadoJson = JsonSerializer.Serialize(state),
        EstadoVersion = state.EstadoVersion,
        ActualizadoEn = now,
        ExpiraEn = now.Add(StateLifetime)
    };

    private static FacturaConversationState? TryDeserialize(string json, int userId, string sessionId)
    {
        FacturaConversationState? state;
        try
        {
            state = JsonSerializer.Deserialize<FacturaConversationState>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (state is null)
            return null;

        state.UserId = userId;
        state.SessionId = sessionId;
        return state;
    }
}
