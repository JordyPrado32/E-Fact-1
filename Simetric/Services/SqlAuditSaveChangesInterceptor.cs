using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Simetric.Models;

namespace Simetric.Services;

public sealed class SqlAuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly AuditActorResolver _auditActorResolver;
    private readonly AuditService _auditService;
    private readonly ConcurrentDictionary<Guid, List<PendingAuditEntry>> _pendingAudits = new();

    public SqlAuditSaveChangesInterceptor(
        AuditActorResolver auditActorResolver,
        AuditService auditService)
    {
        _auditActorResolver = auditActorResolver;
        _auditService = auditService;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CapturePendingAudits(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CapturePendingAudits(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        PersistPendingAuditsAsync(eventData.Context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PersistPendingAuditsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ClearPendingAudits(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ClearPendingAudits(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void CapturePendingAudits(DbContext? context)
    {
        if (context == null || !_auditService.IsEnabled || SqlAuditScope.IsSuppressed)
        {
            ClearPendingAudits(context);
            return;
        }

        var userId = _auditActorResolver.ResolveCurrentUserId();

        var pendingEntries = context.ChangeTracker
            .Entries()
            .Where(ShouldAuditEntry)
            .Select(entry => CreatePendingAuditEntry(entry, userId))
            .Where(entry => entry != null)
            .Cast<PendingAuditEntry>()
            .ToList();

        if (pendingEntries.Count == 0)
        {
            ClearPendingAudits(context);
            return;
        }

        _pendingAudits[context.ContextId.InstanceId] = pendingEntries;
    }

    private async Task PersistPendingAuditsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null ||
            SqlAuditScope.IsSuppressed ||
            !_pendingAudits.TryRemove(context.ContextId.InstanceId, out var pendingEntries) ||
            pendingEntries.Count == 0)
        {
            return;
        }

        foreach (var pendingEntry in pendingEntries)
        {
            var currentValues = pendingEntry.Action == "ELIMINAR"
                ? null
                : CapturePropertyValues(
                    pendingEntry.Entry,
                    useOriginalValues: false,
                    propertyNames: pendingEntry.TrackedPropertyNames);

            var keyValues = pendingEntry.Action == "ELIMINAR"
                ? pendingEntry.KeyValues
                : CaptureKeyValues(pendingEntry.Entry);

            var details = BuildDetailsDocument(pendingEntry, keyValues);

            await _auditService.RegistrarAuditoriaAsync(
                pendingEntry.UserId,
                pendingEntry.Action,
                pendingEntry.PreviousValues,
                currentValues,
                details,
                cancellationToken);
        }
    }

    private void ClearPendingAudits(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        _pendingAudits.TryRemove(context.ContextId.InstanceId, out _);
    }

    private bool ShouldAuditEntry(EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            return false;
        }

        if (entry.Metadata.IsOwned())
        {
            return false;
        }

        if (entry.Entity is Auditoria)
        {
            return false;
        }

        var tableName = entry.Metadata.GetTableName();
        if (string.Equals(tableName, "Auditoria", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private PendingAuditEntry? CreatePendingAuditEntry(EntityEntry entry, int? userId)
    {
        var trackedPropertyNames = entry.State switch
        {
            EntityState.Modified => entry.Properties
                .Where(property =>
                    property.IsModified &&
                    !property.Metadata.IsShadowProperty() &&
                    !Equals(property.OriginalValue, property.CurrentValue))
                .Select(property => property.Metadata.Name)
                .Distinct()
                .ToList(),
            _ => entry.Properties
                .Where(property => !property.Metadata.IsShadowProperty())
                .Select(property => property.Metadata.Name)
                .Distinct()
                .ToList()
        };

        if (entry.State == EntityState.Modified && trackedPropertyNames.Count == 0)
        {
            return null;
        }

        var previousValues = entry.State == EntityState.Added
            ? null
            : CapturePropertyValues(entry, useOriginalValues: true, propertyNames: trackedPropertyNames);

        return new PendingAuditEntry(
            entry,
            userId,
            ResolveAction(entry.State),
            entry.Metadata.ClrType.Name,
            entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
            trackedPropertyNames,
            previousValues,
            CaptureKeyValues(entry),
            _auditActorResolver.ResolveRequestPath(),
            _auditActorResolver.ResolveRemoteIpAddress());
    }

    private static Dictionary<string, object?> CapturePropertyValues(
        EntityEntry entry,
        bool useOriginalValues,
        IReadOnlyCollection<string> propertyNames)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() || !propertyNames.Contains(property.Metadata.Name))
            {
                continue;
            }

            var rawValue = useOriginalValues ? property.OriginalValue : property.CurrentValue;
            values[property.Metadata.Name] = NormalizeAuditValue(rawValue);
        }

        return values;
    }

    private static Dictionary<string, object?> CaptureKeyValues(EntityEntry entry)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var key = entry.Metadata.FindPrimaryKey();

        if (key == null)
        {
            return values;
        }

        foreach (var property in key.Properties)
        {
            var propertyEntry = entry.Property(property.Name);
            var rawValue = entry.State == EntityState.Deleted
                ? propertyEntry.OriginalValue
                : propertyEntry.CurrentValue;

            values[property.Name] = NormalizeAuditValue(rawValue);
        }

        return values;
    }

    private static Dictionary<string, object?> BuildDetailsDocument(
        PendingAuditEntry pendingEntry,
        Dictionary<string, object?> keyValues)
    {
        var details = new Dictionary<string, object?>
        {
            ["Entidad"] = pendingEntry.EntityName,
            ["Tabla"] = pendingEntry.TableName,
            ["Llaves"] = keyValues
        };

        if (pendingEntry.TrackedPropertyNames.Count > 0)
        {
            details["CamposAfectados"] = pendingEntry.TrackedPropertyNames.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(pendingEntry.RequestPath))
        {
            details["Ruta"] = pendingEntry.RequestPath;
        }

        if (!string.IsNullOrWhiteSpace(pendingEntry.RemoteIpAddress))
        {
            details["DireccionIP"] = pendingEntry.RemoteIpAddress;
        }

        return details;
    }

    private static string ResolveAction(EntityState state)
    {
        return state switch
        {
            EntityState.Added => "CREAR",
            EntityState.Modified => "MODIFICAR",
            EntityState.Deleted => "ELIMINAR",
            _ => state.ToString().ToUpperInvariant()
        };
    }

    private static object? NormalizeAuditValue(object? value)
    {
        return value switch
        {
            null => null,
            byte[] bytes => $"[binario: {bytes.Length} bytes]",
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            Enum enumValue => enumValue.ToString(),
            _ => value
        };
    }

    private sealed record PendingAuditEntry(
        EntityEntry Entry,
        int? UserId,
        string Action,
        string EntityName,
        string TableName,
        IReadOnlyCollection<string> TrackedPropertyNames,
        Dictionary<string, object?>? PreviousValues,
        Dictionary<string, object?> KeyValues,
        string? RequestPath,
        string? RemoteIpAddress);
}
