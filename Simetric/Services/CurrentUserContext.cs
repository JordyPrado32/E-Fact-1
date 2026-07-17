using System.Threading;

namespace Simetric.Services;

public sealed class CurrentUserContext
{
    private readonly AsyncLocal<int?> _userId = new();

    public int? UserId => _userId.Value;

    public void SetUserId(int? userId)
    {
        _userId.Value = userId;
    }

    public void Clear()
    {
        _userId.Value = null;
    }
}
