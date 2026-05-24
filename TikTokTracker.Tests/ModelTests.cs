using TikTokTracker.Web.Models;
using Xunit;

namespace TikTokTracker.Tests;

public class ModelTests
{
    // --- GiftTransaction ---

    [Fact]
    public void GiftTransaction_TotalDiamonds_ShouldBeAmountTimesDiamondCost()
    {
        var transaction = new GiftTransaction { Amount = 5, DiamondCost = 10 };
        Assert.Equal(50, transaction.TotalDiamonds);
    }

    [Fact]
    public void GiftTransaction_TotalDiamonds_ShouldBeZero_WhenAmountIsZero()
    {
        var transaction = new GiftTransaction { Amount = 0, DiamondCost = 100 };
        Assert.Equal(0, transaction.TotalDiamonds);
    }

    [Fact]
    public void GiftTransaction_TotalDiamonds_ShouldBeZero_WhenDiamondCostIsZero()
    {
        var transaction = new GiftTransaction { Amount = 10, DiamondCost = 0 };
        Assert.Equal(0, transaction.TotalDiamonds);
    }

    [Fact]
    public void GiftTransaction_DefaultValues_ShouldBeCorrect()
    {
        var transaction = new GiftTransaction();
        Assert.Equal(string.Empty, transaction.SenderUserId);
        Assert.Equal(string.Empty, transaction.SenderUsername);
        Assert.Equal(string.Empty, transaction.SenderNickname);
        Assert.Equal(string.Empty, transaction.GiftName);
        Assert.Null(transaction.StreakId);
    }

    // --- TikTokAccount ---

    [Fact]
    public void TikTokAccount_StreamUrl_ShouldContainUsername()
    {
        var account = new TikTokAccount { Username = "testuser" };
        Assert.Equal("https://www.tiktok.com/@testuser/live", account.StreamUrl);
    }

    [Fact]
    public void TikTokAccount_ProfileUrl_ShouldContainUsername()
    {
        var account = new TikTokAccount { Username = "testuser" };
        Assert.Equal("https://www.tiktok.com/@testuser", account.ProfileUrl);
    }

    [Fact]
    public void TikTokAccount_DefaultValues_ShouldBeCorrect()
    {
        var account = new TikTokAccount();
        Assert.Equal(string.Empty, account.Username);
        Assert.False(account.IsOnline);
        Assert.Equal(0, account.CoinsToday);
        Assert.Equal(0, account.ViewerCount);
        Assert.False(account.AutoRecord);
        Assert.False(account.IsRecording);
        Assert.Null(account.ProfileImageUrl);
    }

    // --- GifterSummary ---

    [Fact]
    public void GifterSummary_DefaultValues_ShouldBeCorrect()
    {
        var summary = new GifterSummary();
        Assert.Equal(string.Empty, summary.SenderUserId);
        Assert.Equal(string.Empty, summary.SenderUsername);
        Assert.Equal(string.Empty, summary.SenderNickname);
        Assert.Equal(0, summary.TotalDiamonds);
        Assert.Equal(0, summary.TotalGifts);
    }

    // --- DailyCoinEarning ---

    [Fact]
    public void DailyCoinEarning_ShouldHoldAccountRelationship()
    {
        var account = new TikTokAccount { Id = 1, Username = "user1" };
        var earning = new DailyCoinEarning
        {
            TikTokAccountId = 1,
            Account = account,
            Date = new DateTime(2026, 3, 26),
            Coins = 500
        };

        Assert.Equal(1, earning.TikTokAccountId);
        Assert.Equal("user1", earning.Account.Username);
        Assert.Equal(500, earning.Coins);
    }

    // --- SystemSetting ---

    [Fact]
    public void SystemSetting_ShouldHoldKeyValuePair()
    {
        var setting = new SystemSetting { Key = "TestKey", Value = "TestValue" };
        Assert.Equal("TestKey", setting.Key);
        Assert.Equal("TestValue", setting.Value);
    }
}
