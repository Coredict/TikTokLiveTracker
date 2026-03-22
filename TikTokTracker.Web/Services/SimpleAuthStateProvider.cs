using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace TikTokTracker.Web.Services;

public class SimpleAuthStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(new AuthenticationState(_principal));
    }

    public void Login(string password, string expectedPassword)
    {
        if (password == expectedPassword)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Admin") }, "Password");
            _principal = new ClaimsPrincipal(identity);
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }

    public void Logout()
    {
        _principal = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
