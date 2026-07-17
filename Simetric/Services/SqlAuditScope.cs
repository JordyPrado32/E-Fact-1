using System.Threading;

namespace Simetric.Services;

public static class SqlAuditScope
{
    private static readonly AsyncLocal<int> SuppressionDepth = new();

    public static bool IsSuppressed => SuppressionDepth.Value > 0;

    public static IDisposable Suppress()
    {
        SuppressionDepth.Value++;
        return new RestoreScope();
    }

    private sealed class RestoreScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (SuppressionDepth.Value > 0)
                SuppressionDepth.Value--;
        }
    }
}
