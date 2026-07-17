using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Simetric.Tutorials;

namespace Simetric.Services;

public sealed class TutorialStateService
{
    private const string StoragePrefix = "numerica:tutorial:";
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IJSRuntime _jsRuntime;

    public TutorialStateService(
        AuthenticationStateProvider authenticationStateProvider,
        IJSRuntime jsRuntime)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
    }

    public async Task<TutorialProgress> GetProgressAsync(string tutorialId)
    {
        try
        {
            var storageKey = await BuildStorageKeyAsync(tutorialId);
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", storageKey);

            if (string.IsNullOrWhiteSpace(json))
                return new TutorialProgress();

            return JsonSerializer.Deserialize<TutorialProgress>(json) ?? new TutorialProgress();
        }
        catch (JSDisconnectedException)
        {
            return new TutorialProgress();
        }
        catch (InvalidOperationException)
        {
            return new TutorialProgress();
        }
    }

    public async Task<Dictionary<string, TutorialProgress>> GetProgressMapAsync(IEnumerable<string> tutorialIds)
    {
        var result = new Dictionary<string, TutorialProgress>(StringComparer.OrdinalIgnoreCase);
        var ids = tutorialIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
            return result;

        try
        {
            var storagePrefix = await BuildStoragePrefixAsync();
            var keysByTutorialId = ids.ToDictionary(
                tutorialId => tutorialId,
                tutorialId => $"{storagePrefix}{tutorialId.ToLowerInvariant()}",
                StringComparer.OrdinalIgnoreCase);

            var storedValues = await _jsRuntime.InvokeAsync<Dictionary<string, string?>>(
                "numericaUi.getLocalStorageItems",
                keysByTutorialId.Values.ToArray());

            foreach (var tutorialId in ids)
            {
                var storageKey = keysByTutorialId[tutorialId];
                result[tutorialId] = storedValues.TryGetValue(storageKey, out var json) && !string.IsNullOrWhiteSpace(json)
                    ? JsonSerializer.Deserialize<TutorialProgress>(json) ?? new TutorialProgress()
                    : new TutorialProgress();
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (JsonException)
        {
        }

        foreach (var tutorialId in ids)
            result.TryAdd(tutorialId, new TutorialProgress());

        return result;
    }

    public async Task<bool> ShouldAutoStartAsync(string tutorialId)
    {
        var progress = await GetProgressAsync(tutorialId);
        return !progress.Completed && !progress.AutoShowDisabled;
    }

    public async Task MarkOpenedAsync(string tutorialId)
    {
        var progress = await GetProgressAsync(tutorialId);
        progress.LastOpenedAt = DateTimeOffset.UtcNow;
        progress.LastStepIndex ??= 0;
        await SaveProgressAsync(tutorialId, progress);
    }

    public async Task SaveCloseProgressAsync(string tutorialId, int? lastStepIndex, bool completed, bool autoShowDisabled)
    {
        var progress = await GetProgressAsync(tutorialId);
        progress.LastOpenedAt = DateTimeOffset.UtcNow;

        if (lastStepIndex.HasValue && lastStepIndex.Value >= 0)
            progress.LastStepIndex = lastStepIndex.Value;

        if (completed)
        {
            progress.Completed = true;
            progress.CompletedAt = DateTimeOffset.UtcNow;
        }

        if (autoShowDisabled)
        {
            progress.AutoShowDisabled = true;
            progress.AutoShowDisabledAt = DateTimeOffset.UtcNow;
        }

        await SaveProgressAsync(tutorialId, progress);
    }

    public Task MarkCompletedAsync(string tutorialId, bool autoShowDisabled) =>
        SaveCloseProgressAsync(tutorialId, null, completed: true, autoShowDisabled: autoShowDisabled);

    public async Task DisableAutoShowAsync(string tutorialId)
    {
        var progress = await GetProgressAsync(tutorialId);
        progress.AutoShowDisabled = true;
        progress.AutoShowDisabledAt = DateTimeOffset.UtcNow;
        await SaveProgressAsync(tutorialId, progress);
    }

    private async Task SaveProgressAsync(string tutorialId, TutorialProgress progress)
    {
        try
        {
            var storageKey = await BuildStorageKeyAsync(tutorialId);
            var json = JsonSerializer.Serialize(progress);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", storageKey, json);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task<string> BuildStorageKeyAsync(string tutorialId)
    {
        var storagePrefix = await BuildStoragePrefixAsync();
        return $"{storagePrefix}{tutorialId.Trim().ToLowerInvariant()}";
    }

    private async Task<string> BuildStoragePrefixAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        var userKey =
            user.FindFirst("IdUsuario")?.Value ??
            user.FindFirst(ClaimTypes.Email)?.Value ??
            user.Identity?.Name ??
            "anon";

        return $"{StoragePrefix}{userKey}:";
    }
}
