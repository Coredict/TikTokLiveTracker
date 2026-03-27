using CliWrap;
using System.Collections.Concurrent;
using System.Linq;
using TikTokTracker.Recorder.Models;

namespace TikTokTracker.Recorder.Services;

public interface IRecordingService
{
    Task<bool> StartRecordingAsync(string username, string streamUrl);
    bool StopRecordingAsync(string username);
    List<ActiveRecordingInfo> GetActiveRecordings();
}

public class RecordingService : IRecordingService
{
    private readonly ILogger<RecordingService> _logger;
    private readonly ConcurrentDictionary<string, (CancellationTokenSource Cts, string Filename, string FilePath, DateTime StartedAt)> _activeRecordings = new();
    private readonly string _recordingsDir = "recordings";
    private readonly string _tempDir = "tmp";

    public RecordingService(ILogger<RecordingService> logger)
    {
        _logger = logger;
        if (!Directory.Exists(_recordingsDir))
        {
            Directory.CreateDirectory(_recordingsDir);
        }
        if (!Directory.Exists(_tempDir))
        {
            Directory.CreateDirectory(_tempDir);
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
        var filepath = Path.Combine(_tempDir, filename);

        _logger.LogInformation("Starting ffmpeg for {Username} -> {Filename} (in {TempDir})", username, filename, _tempDir);

        try
        {
            var cts = new CancellationTokenSource();
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await Cli.Wrap("ffmpeg")
                        .WithArguments(new[] { "-i", streamUrl, "-c", "copy", "-f", "mp4", "-movflags", "frag_keyframe+empty_moov+default_base_moof", "-y", filepath })
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync(cts.Token);
                    
                    _logger.LogInformation("ffmpeg process for {Username} finished with code {ExitCode}", username, result.ExitCode);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Recording for {Username} cancelled.", username);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ffmpeg process for {Username} failed", username);
                }
                finally
                {
                    if (_activeRecordings.TryRemove(username, out var info))
                    {
                        var finalPath = Path.Combine(_recordingsDir, info.Filename);
                        try
                        {
                            if (File.Exists(info.FilePath))
                            {
                                File.Move(info.FilePath, finalPath, true);
                                _logger.LogInformation("Moved completed recording for {Username} to {FinalPath}", username, finalPath);
                            }
                        }
                        catch (Exception moveEx)
                        {
                            _logger.LogError(moveEx, "Failed to move recording file from {TempPath} to {FinalPath}", info.FilePath, finalPath);
                        }
                    }
                }
            });

            _activeRecordings[username] = (cts, filename, filepath, DateTime.Now);
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

    public List<ActiveRecordingInfo> GetActiveRecordings()
    {
        return _activeRecordings.Select(kvp => new ActiveRecordingInfo(kvp.Key, kvp.Value.StartedAt))
            .OrderBy(r => r.username)
            .ToList();
    }
}
