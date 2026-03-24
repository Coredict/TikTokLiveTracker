using CliWrap;
using System.Collections.Concurrent;

namespace TikTokTracker.Recorder.Services;

public interface IRecordingService
{
    Task<bool> StartRecordingAsync(string username, string streamUrl);
    bool StopRecordingAsync(string username);
    List<string> GetActiveRecordings();
}

public class RecordingService : IRecordingService
{
    private readonly ILogger<RecordingService> _logger;
    private readonly ConcurrentDictionary<string, (CancellationTokenSource Cts, string Filename, string FilePath)> _activeRecordings = new();
    private readonly string _recordingsDir = "recordings";

    public RecordingService(ILogger<RecordingService> logger)
    {
        _logger = logger;
        if (!Directory.Exists(_recordingsDir))
        {
            Directory.CreateDirectory(_recordingsDir);
        }
    }

    public async Task<bool> StartRecordingAsync(string username, string streamUrl)
    {
        if (_activeRecordings.ContainsKey(username))
        {
            _logger.LogWarning("Recording already active for {Username}", username);
            return false;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filename = $"{username}_{timestamp}.mp4";
        var filepath = Path.Combine(_recordingsDir, filename);

        _logger.LogInformation("Starting ffmpeg for {Username} -> {Filename}", username, filename);

        try
        {
            var cts = new CancellationTokenSource();
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await Cli.Wrap("ffmpeg")
                        .WithArguments(new[] { "-i", streamUrl, "-c", "copy", "-f", "mp4", "-y", filepath })
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync(cts.Token);
                    
                    if (_activeRecordings.TryRemove(username, out _))
                    {
                        _logger.LogInformation("Recording for {Username} finished: {ExitCode}", username, result.ExitCode);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Recording for {Username} cancelled.", username);
                    if (_activeRecordings.TryRemove(username, out _))
                    {
                        // Clean up
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ffmpeg process for {Username} failed", username);
                    if (_activeRecordings.TryRemove(username, out _))
                    {
                    }
                }
            });

            _activeRecordings[username] = (cts, filename, filepath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ffmpeg for {Username}", username);
            return false;
        }
    }

    public bool StopRecordingAsync(string username)
    {
        if (_activeRecordings.TryGetValue(username, out var info))
        {
            _logger.LogInformation("Stopping recording for {Username}", username);
            info.Cts.Cancel();
            return true;
        }

        return false;
    }

    public List<string> GetActiveRecordings()
    {
        return _activeRecordings.Keys.OrderBy(k => k).ToList();
    }
}
