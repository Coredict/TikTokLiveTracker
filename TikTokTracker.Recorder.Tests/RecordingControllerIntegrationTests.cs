using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;
using Xunit;

namespace TikTokTracker.Recorder.Tests;

/// <summary>
/// End-to-end integration tests for the recorder-service API endpoints.
/// Uses WebApplicationFactory with mocked IRecordingService and ITikTokUrlProvider
/// to test HTTP request/response behavior without requiring ffmpeg or yt-dlp.
///
/// Each test creates its own HttpClient and resets mocks to prevent state bleed.
/// </summary>
public class RecordingControllerIntegrationTests : IClassFixture<RecordingControllerIntegrationTests.RecorderWebAppFactory>
{
    private readonly RecorderWebAppFactory _factory;
    private readonly Mock<IRecordingService> _recordingServiceMock;
    private readonly Mock<ITikTokUrlProvider> _urlProviderMock;

    public RecordingControllerIntegrationTests(RecorderWebAppFactory factory)
    {
        _factory = factory;
        _recordingServiceMock = factory.RecordingServiceMock;
        _urlProviderMock = factory.UrlProviderMock;

        // Reset all mock state before each test so setups don't bleed
        _recordingServiceMock.Reset();
        _urlProviderMock.Reset();
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    // --- GET / (health/index) ---

    [Fact]
    public async Task Index_ShouldReturnOkWithStatusMessage()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", content);
    }

    // --- GET /record ---

    [Fact]
    public async Task GetActiveRecordings_ShouldReturnEmptyList_WhenNoActiveRecordings()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>());

        using var client = CreateClient();
        var response = await client.GetAsync("/record");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var recordings = doc.RootElement.GetProperty("active_recordings");
        Assert.Equal(0, recordings.GetArrayLength());
    }

    [Fact]
    public async Task GetActiveRecordings_ShouldReturnActiveRecordings()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>
            {
                new("testuser", DateTime.UtcNow, 1024),
                new("anotheruser", DateTime.UtcNow, 2048)
            });

        using var client = CreateClient();
        var response = await client.GetAsync("/record");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var recordings = doc.RootElement.GetProperty("active_recordings");
        Assert.Equal(2, recordings.GetArrayLength());
    }

    // --- POST /record/{username} ---

    [Fact]
    public async Task StartRecording_ShouldReturnOk_WhenUserIsLive()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>());
        _urlProviderMock.Setup(u => u.GetStreamUrlAsync("liveuser"))
            .ReturnsAsync("https://stream.tiktok.com/some-stream-url");
        _recordingServiceMock.Setup(s => s.StartRecordingAsync("liveuser", It.IsAny<string>()))
            .ReturnsAsync(true);

        using var client = CreateClient();
        var response = await client.PostAsync("/record/liveuser", null);

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("recording_started", content);
    }

    [Fact]
    public async Task StartRecording_ShouldReturnOk_WhenAlreadyRecording()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>
            {
                new("alreadyrec", DateTime.UtcNow, 512)
            });

        using var client = CreateClient();
        var response = await client.PostAsync("/record/alreadyrec", null);

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("already_recording", content);
    }

    [Fact]
    public async Task StartRecording_ShouldReturnNotFound_WhenUserIsOffline()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>());
        _urlProviderMock.Setup(u => u.GetStreamUrlAsync("offlineuser"))
            .ReturnsAsync((string?)null);

        using var client = CreateClient();
        var response = await client.PostAsync("/record/offlineuser", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartRecording_ShouldReturn500_WhenRecordingFailsToStart()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>());
        _urlProviderMock.Setup(u => u.GetStreamUrlAsync("failuser"))
            .ReturnsAsync("https://stream.tiktok.com/fail-url");
        _recordingServiceMock.Setup(s => s.StartRecordingAsync("failuser", It.IsAny<string>()))
            .ReturnsAsync(false);

        using var client = CreateClient();
        var response = await client.PostAsync("/record/failuser", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task StartRecording_ShouldStripAtSymbol_FromUsername()
    {
        _recordingServiceMock.Setup(s => s.GetActiveRecordings())
            .Returns(new List<ActiveRecordingInfo>());
        _urlProviderMock.Setup(u => u.GetStreamUrlAsync("cleanuser"))
            .ReturnsAsync("https://stream.tiktok.com/clean-url");
        _recordingServiceMock.Setup(s => s.StartRecordingAsync("cleanuser", It.IsAny<string>()))
            .ReturnsAsync(true);

        using var client = CreateClient();
        var response = await client.PostAsync("/record/@cleanuser", null);

        response.EnsureSuccessStatusCode();
        _urlProviderMock.Verify(u => u.GetStreamUrlAsync("cleanuser"), Times.Once);
    }

    // --- DELETE /record/{username} ---

    [Fact]
    public async Task StopRecording_ShouldReturnOk_WhenRecordingExists()
    {
        _recordingServiceMock.Setup(s => s.StopRecordingAsync("stopuser"))
            .Returns(true);

        using var client = CreateClient();
        var response = await client.DeleteAsync("/record/stopuser");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("recording_stopped", content);
    }

    [Fact]
    public async Task StopRecording_ShouldReturnNotFound_WhenNoActiveRecording()
    {
        _recordingServiceMock.Setup(s => s.StopRecordingAsync("norecording"))
            .Returns(false);

        using var client = CreateClient();
        var response = await client.DeleteAsync("/record/norecording");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- WebApplicationFactory ---

    public class RecorderWebAppFactory : WebApplicationFactory<Program>
    {
        public Mock<IRecordingService> RecordingServiceMock { get; } = new();
        public Mock<ITikTokUrlProvider> UrlProviderMock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove real service registrations that we're replacing or don't need in tests
                var typesToRemove = new[]
                {
                    typeof(IRecordingService),
                    typeof(ITikTokUrlProvider),
                    typeof(IFfmpegRunner),
                    typeof(IHostedService)
                };

                var descriptorsToRemove = services
                    .Where(d => typesToRemove.Contains(d.ServiceType))
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // Replace with mocks
                services.AddSingleton(RecordingServiceMock.Object);
                services.AddSingleton(UrlProviderMock.Object);
            });
        }
    }
}
