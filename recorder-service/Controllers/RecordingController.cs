using Microsoft.AspNetCore.Mvc;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;

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
        
        if (_recordingService.GetActiveRecordings().Contains(username, StringComparer.OrdinalIgnoreCase))
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
    public ActionResult<FilesResponse> GetRecordedFiles()
    {
        if (!Directory.Exists(_recordingsDir))
        {
            return Ok(new FilesResponse(new List<string>()));
        }
        
        var files = Directory.GetFiles(_recordingsDir, "*.mp4")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderByDescending(f => f)
            .ToList();
            
        return Ok(new FilesResponse(files));
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
