using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TikTokTracker.Recorder.Services;

public class RecordingCleanupService : BackgroundService
{
    private readonly ILogger<RecordingCleanupService> _logger;
    private readonly string _recordingsDir = "recordings";
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public RecordingCleanupService(ILogger<RecordingCleanupService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recording Cleanup Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during recording cleanup.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_recordingsDir)) return;

        var files = Directory.GetFiles(_recordingsDir, "*.mp4");
        _logger.LogInformation("Scanning {Count} files for short recordings...", files.Length);

        int deletedCount = 0;
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var duration = await GetVideoDurationAsync(file, ct);
                if (duration.HasValue && duration.Value < 2.0)
                {
                    File.Delete(file);
                    _logger.LogWarning("Deleted short recording: {File} (Duration: {Duration:F2}s)", Path.GetFileName(file), duration.Value);
                    deletedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file during cleanup: {File}", file);
            }
        }

        if (deletedCount > 0)
        {
            _logger.LogInformation("Cleanup finished. Deleted {Count} short recordings.", deletedCount);
        }
    }

    private async Task<double?> GetVideoDurationAsync(string filepath, CancellationToken ct)
    {
        try
        {
            var result = await Cli.Wrap("ffprobe")
                .WithArguments(new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", filepath })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(ct);

            if (result.ExitCode == 0 && double.TryParse(result.StandardOutput.Trim(), out var duration))
            {
                return duration;
            }
        }
        catch (Exception)
        {
            // Ignore error, return null
        }
        return null;
    }
}
