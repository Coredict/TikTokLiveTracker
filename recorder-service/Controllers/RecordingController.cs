using Microsoft.AspNetCore.Mvc;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;
using CliWrap;
using CliWrap.Buffered;
using System.Linq;

namespace TikTokTracker.Recorder.Controllers;

[ApiController]
[Route("")]
public class RecordingController : ControllerBase
{
    private readonly IRecordingService _recordingService;
    private readonly ITikTokUrlProvider _urlProvider;
    private readonly string _recordingsDir = "recordings";

    public RecordingController(IRecordingService recordingService, ITikTokUrlProvider urlProvider)
    {
        _recordingService = recordingService;
        _urlProvider = urlProvider;

        if (!Directory.Exists(_recordingsDir))
        {
            Directory.CreateDirectory(_recordingsDir);
        }
    }

    [HttpGet]
    public IActionResult Index()
    {
        return Ok(new { status = "ok", message = "TikTok Live Recorder API is running" });
    }

    // --- RECORDING SESSIONS ---

    [HttpGet("record")]
    public ActionResult<ActiveRecordingsResponse> GetActiveRecordings()
    {
        return new ActiveRecordingsResponse(_recordingService.GetActiveRecordings());
    }

    [HttpPost("record/{username}")]
    public async Task<IActionResult> StartRecording(string username)
    {
        username = username.Trim().Replace("@", "");
        
        if (_recordingService.GetActiveRecordings().Any(r => r.username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            return Ok(new { status = "already_recording", username });
        }

        var streamUrl = await _urlProvider.GetStreamUrlAsync(username);
        if (string.IsNullOrEmpty(streamUrl))
        {
            return NotFound(new { detail = $"User {username} is offline or stream URL not found." });
        }

        var success = await _recordingService.StartRecordingAsync(username, streamUrl);
        if (!success)
        {
            return StatusCode(500, new { detail = "Failed to start recording process." });
        }

        return Ok(new { status = "recording_started", username });
    }

    [HttpDelete("record/{username}")]
    public IActionResult StopRecording(string username)
    {
        username = username.Trim().Replace("@", "");
        var success = _recordingService.StopRecordingAsync(username);
        if (!success)
        {
            return NotFound(new { detail = $"No active recording found for {username}" });
        }
        
        return Ok(new { status = "recording_stopped", username });
    }

    // --- RECORDED FILES ---

    [HttpGet("recordings")]
    public async Task<ActionResult<FilesResponse>> GetRecordedFiles()
    {
        if (!Directory.Exists(_recordingsDir))
        {
            return Ok(new FilesResponse(new List<VideoFileInfo>()));
        }
        
        var fileNames = Directory.GetFiles(_recordingsDir, "*.mp4")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderByDescending(f => f)
            .ToList();

        var videoFiles = new List<VideoFileInfo>();
        foreach (var fileName in fileNames)
        {
            var filePath = Path.Combine(_recordingsDir, fileName);
            var fileInfo = new FileInfo(filePath);
            var duration = await GetVideoDurationAsync(filePath);
            videoFiles.Add(new VideoFileInfo(fileName, fileInfo.Length, duration));
        }
            
        return Ok(new FilesResponse(videoFiles));
    }

    private async Task<double?> GetVideoDurationAsync(string filepath)
    {
        try
        {
            var result = await Cli.Wrap("ffprobe")
                .WithArguments(new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", filepath })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

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

    [HttpGet("recordings/{filename}")]
    public IActionResult DownloadRecording(string filename)
    {
        var filepath = Path.Combine(_recordingsDir, filename);
        if (!System.IO.File.Exists(filepath))
        {
            return NotFound(new { detail = "File not found" });
        }
        
        return File(System.IO.File.OpenRead(filepath), "video/mp4", filename);
    }

    [HttpDelete("recordings/{filename}")]
    public IActionResult DeleteRecording(string filename)
    {
        var filepath = Path.Combine(_recordingsDir, filename);
        if (!System.IO.File.Exists(filepath))
        {
            return NotFound(new { detail = "File not found" });
        }
        
        try
        {
            System.IO.File.Delete(filepath);
            return Ok(new { status = "deleted", filename });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { detail = $"Failed to delete file: {e.Message}" });
        }
    }
}
