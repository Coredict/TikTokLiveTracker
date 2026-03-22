using Microsoft.AspNetCore.Components;

namespace TikTokTracker.Web.Services;

/// <summary>
/// Per-circuit singleton (Scoped) that tracks whether this browser session is logged in as admin.
/// </summary>
public class AdminSessionService
{
    private bool _isAdmin;
    public bool IsAdmin => _isAdmin;
    public event Action? OnChange;

    public bool Login(string password, string expectedPassword)
    {
        if (password == expectedPassword)
        {
            _isAdmin = true;
            OnChange?.Invoke();
            return true;
        }
        return false;
    }

    public void Logout()
    {
        _isAdmin = false;
        OnChange?.Invoke();
    }
}
