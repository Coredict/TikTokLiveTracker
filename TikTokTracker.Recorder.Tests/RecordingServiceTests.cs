using Microsoft.Extensions.Logging;
using Moq;
using TikTokTracker.Recorder.Services;
using Xunit;

namespace TikTokTracker.Recorder.Tests;

/// <summary>
/// Unit tests for RecordingService — uses a fake IFfmpegRunner that blocks
/// until cancelled, so entries stay in _activeRecordings deterministically.
/// </summary>
public class RecordingServiceTests : IDisposable
{
    private readonly RecordingService _service;

    /// <summary>
    /// Fake ffmpeg runner that blocks indefinitely until the token is cancelled,
    /// simulating a long-running stream recording without spawning a real process.
    /// </summary>
    private class BlockingFfmpegRunner : IFfmpegRunner
    {
        public async Task<(int ExitCode, string StdErr)> RunAsync(string streamUrl, string outputPath, CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation — simulate clean exit
            }
            return (0, string.Empty);
        }
    }

    public RecordingServiceTests()
    {
        // RecordingService constructor creates "recordings/" and "tmp/" dirs as a side effect.
        // We accept this and clean up in Dispose.
        var loggerMock = new Mock<ILogger<RecordingService>>();
        _service = new RecordingService(loggerMock.Object, new BlockingFfmpegRunner());
    }

    public void Dispose()
    {
        // Stop any lingering recordings so background tasks exit
        foreach (var r in _service.GetActiveRecordings())
        {
            _service.StopRecordingAsync(r.username);
        }

        // Clean up directories created by the constructor
        try { if (Directory.Exists("recordings")) Directory.Delete("recordings", true); } catch { }
        try { if (Directory.Exists("tmp")) Directory.Delete("tmp", true); } catch { }
    }

    [Fact]
    public void GetActiveRecordings_ShouldReturnEmptyList_Initially()
    {
        var recordings = _service.GetActiveRecordings();
        Assert.Empty(recordings);
    }

    [Fact]
    public void StopRecording_ShouldReturnFalse_WhenNoActiveRecording()
    {
        var result = _service.StopRecordingAsync("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task StartRecording_ShouldAddToActiveRecordings()
    {
        var result = await _service.StartRecordingAsync("trackuser", "http://fake-url");

        Assert.True(result);

        // With the blocking fake, the entry stays in _activeRecordings reliably
        var recordings = _service.GetActiveRecordings();
        Assert.Contains(recordings, r => r.username == "trackuser");
    }

    [Fact]
    public async Task StartRecording_ShouldReturnFalse_WhenAlreadyRecording()
    {
        await _service.StartRecordingAsync("testuser", "http://fake-stream-url");

        // Second attempt should return false — entry is still alive thanks to blocking fake
        var result = await _service.StartRecordingAsync("testuser", "http://another-url");

        Assert.False(result);
    }

    [Fact]
    public async Task StopRecording_ShouldReturnTrue_WhenRecordingIsActive()
    {
        await _service.StartRecordingAsync("stopme", "http://fake-url");

        var result = _service.StopRecordingAsync("stopme");

        Assert.True(result);
    }

    [Fact]
    public async Task StopRecording_ShouldRemoveFromActiveRecordings()
    {
        await _service.StartRecordingAsync("removeuser", "http://fake-url");
        Assert.Single(_service.GetActiveRecordings());

        _service.StopRecordingAsync("removeuser");

        // Give the background task a moment to process the cancellation and clean up
        await Task.Delay(100);

        Assert.Empty(_service.GetActiveRecordings());
    }

    [Fact]
    public async Task GetActiveRecordings_ShouldReturnSortedByUsername()
    {
        await _service.StartRecordingAsync("zuser", "http://fake1");
        await _service.StartRecordingAsync("auser", "http://fake2");
        await _service.StartRecordingAsync("muser", "http://fake3");

        var recordings = _service.GetActiveRecordings();

        Assert.Equal(3, recordings.Count);
        Assert.Equal("auser", recordings[0].username);
        Assert.Equal("muser", recordings[1].username);
        Assert.Equal("zuser", recordings[2].username);
    }

    [Fact]
    public async Task StartRecording_MultipleDifferentUsers_ShouldAllBeTracked()
    {
        await _service.StartRecordingAsync("user1", "http://url1");
        await _service.StartRecordingAsync("user2", "http://url2");
        await _service.StartRecordingAsync("user3", "http://url3");

        var recordings = _service.GetActiveRecordings();

        Assert.Equal(3, recordings.Count);
        Assert.Contains(recordings, r => r.username == "user1");
        Assert.Contains(recordings, r => r.username == "user2");
        Assert.Contains(recordings, r => r.username == "user3");
    }
}
