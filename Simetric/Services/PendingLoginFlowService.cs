using System.Collections.Concurrent;

namespace Simetric.Services
{
    public sealed class PendingLoginFlowService
    {
        private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(5);
        private readonly ConcurrentDictionary<string, PendingLoginPayload> _pendingLogins = new();

        public string Create(int userId, string username, string password, bool recordarme)
        {
            CleanupExpired();

            var token = Guid.NewGuid().ToString("N");
            _pendingLogins[token] = new PendingLoginPayload(
                userId,
                username,
                password,
                recordarme,
                DateTime.UtcNow.Add(PendingLifetime));

            return token;
        }

        public bool TryConsume(string token, int expectedUserId, out PendingLoginPayload? payload)
        {
            payload = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (!_pendingLogins.TryRemove(token, out var pendingLogin))
            {
                return false;
            }

            if (pendingLogin.ExpiresAtUtc < DateTime.UtcNow || pendingLogin.UserId != expectedUserId)
            {
                return false;
            }

            payload = pendingLogin;
            return true;
        }

        public bool TryPeek(string token, int expectedUserId, out PendingLoginPayload? payload)
        {
            payload = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (!_pendingLogins.TryGetValue(token, out var pendingLogin))
            {
                return false;
            }

            if (pendingLogin.ExpiresAtUtc < DateTime.UtcNow || pendingLogin.UserId != expectedUserId)
            {
                return false;
            }

            payload = pendingLogin;
            return true;
        }

        private void CleanupExpired()
        {
            var now = DateTime.UtcNow;

            foreach (var item in _pendingLogins)
            {
                if (item.Value.ExpiresAtUtc < now)
                {
                    _pendingLogins.TryRemove(item.Key, out _);
                }
            }
        }
    }

    public sealed record PendingLoginPayload(
        int UserId,
        string Username,
        string Password,
        bool Recordarme,
        DateTime ExpiresAtUtc);
}
