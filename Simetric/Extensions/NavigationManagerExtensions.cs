using Microsoft.AspNetCore.Components;

namespace Simetric.Extensions;

public static class NavigationManagerExtensions
{
    public static bool IsCurrentTarget(this NavigationManager navigationManager, string target)
    {
        var current = BuildComparableUri(navigationManager, navigationManager.Uri);
        var destination = BuildComparableUri(navigationManager, navigationManager.ToAbsoluteUri(target).ToString());

        return string.Equals(current, destination, StringComparison.OrdinalIgnoreCase);
    }

    public static void NavigateToIfNeeded(
        this NavigationManager navigationManager,
        string target,
        bool forceLoad = false,
        bool replace = false)
    {
        if (navigationManager.IsCurrentTarget(target))
            return;

        navigationManager.NavigateTo(target, forceLoad, replace);
    }

    private static string BuildComparableUri(NavigationManager navigationManager, string uri)
    {
        var absoluteUri = navigationManager.ToAbsoluteUri(uri);
        var path = absoluteUri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
            path = "/";

        return $"{path}{absoluteUri.Query}";
    }
}
