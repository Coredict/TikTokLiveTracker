using TikTokTracker.Web.Services;
using Xunit;

namespace TikTokTracker.Tests;

public class AdminSessionServiceTests
{
    [Fact]
    public void IsAdmin_ShouldBeFalseByDefault()
    {
        var service = new AdminSessionService();
        Assert.False(service.IsAdmin);
    }

    [Fact]
    public void Login_WithCorrectPassword_ShouldReturnTrueAndSetIsAdmin()
    {
        var service = new AdminSessionService();

        var result = service.Login("secret123", "secret123");

        Assert.True(result);
        Assert.True(service.IsAdmin);
    }

    [Fact]
    public void Login_WithWrongPassword_ShouldReturnFalseAndNotSetIsAdmin()
    {
        var service = new AdminSessionService();

        var result = service.Login("wrong", "secret123");

        Assert.False(result);
        Assert.False(service.IsAdmin);
    }

    [Fact]
    public void Login_ShouldFireOnChangeEvent_WhenPasswordIsCorrect()
    {
        var service = new AdminSessionService();
        bool eventFired = false;
        service.OnChange += () => eventFired = true;

        service.Login("secret", "secret");

        Assert.True(eventFired);
    }

    [Fact]
    public void Login_ShouldNotFireOnChangeEvent_WhenPasswordIsWrong()
    {
        var service = new AdminSessionService();
        bool eventFired = false;
        service.OnChange += () => eventFired = true;

        service.Login("wrong", "secret");

        Assert.False(eventFired);
    }

    [Fact]
    public void Logout_ShouldSetIsAdminToFalse()
    {
        var service = new AdminSessionService();
        service.Login("pass", "pass"); // Login first
        Assert.True(service.IsAdmin);

        service.Logout();

        Assert.False(service.IsAdmin);
    }

    [Fact]
    public void Logout_ShouldFireOnChangeEvent()
    {
        var service = new AdminSessionService();
        service.Login("pass", "pass");

        bool eventFired = false;
        service.OnChange += () => eventFired = true;

        service.Logout();

        Assert.True(eventFired);
    }

    [Fact]
    public void Login_ThenLogout_ThenLogin_ShouldWorkCorrectly()
    {
        var service = new AdminSessionService();

        service.Login("pass", "pass");
        Assert.True(service.IsAdmin);

        service.Logout();
        Assert.False(service.IsAdmin);

        service.Login("pass", "pass");
        Assert.True(service.IsAdmin);
    }
}
