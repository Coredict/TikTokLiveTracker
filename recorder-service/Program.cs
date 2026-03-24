using Microsoft.AspNetCore.Mvc;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ITikTokUrlProvider, TikTokUrlProvider>();
builder.Services.AddSingleton<IRecordingService, RecordingService>();

var app = builder.Build();

var recordingsDir = "recordings";
if (!Directory.Exists(recordingsDir))
{
    Directory.CreateDirectory(recordingsDir);
}

app.MapGet("/", () => new { status = "ok", message = "TikTok Live Recorder API is running" });

// --- RECORDING SESSIONS ---

app.MapGet("/record", (IRecordingService recordingService) => 
    new ActiveRecordingsResponse(recordingService.GetActiveRecordings()));

app.MapPost("/record/{username}", async (string username, ITikTokUrlProvider urlProvider, IRecordingService recordingService) =>
{
    username = username.Trim().Replace("@", "");
    
    if (recordingService.GetActiveRecordings().Contains(username, StringComparer.OrdinalIgnoreCase))
    {
        return Results.Ok(new { status = "already_recording", username });
    }

    var streamUrl = await urlProvider.GetStreamUrlAsync(username);
    if (string.IsNullOrEmpty(streamUrl))
    {
        return Results.NotFound(new { detail = $"User {username} is offline or stream URL not found." });
    }

    var success = await recordingService.StartRecordingAsync(username, streamUrl);
    if (!success)
    {
        return Results.InternalServerError(new { detail = "Failed to start recording process." });
    }

    return Results.Ok(new { status = "recording_started", username });
});

app.MapDelete("/record/{username}", (string username, IRecordingService recordingService) =>
{
    username = username.Trim().Replace("@", "");
    var success = recordingService.StopRecordingAsync(username);
    if (!success)
    {
        return Results.NotFound(new { detail = $"No active recording found for {username}" });
    }
    
    return Results.Ok(new { status = "recording_stopped", username });
});

// --- RECORDED FILES ---

app.MapGet("/recordings", () =>
{
    if (!Directory.Exists(recordingsDir))
    {
        return Results.Ok(new FilesResponse(new List<string>()));
    }
    
    var files = Directory.GetFiles(recordingsDir, "*.mp4")
        .Select(Path.GetFileName)
        .Where(f => f != null)
        .Cast<string>()
        .OrderByDescending(f => f)
        .ToList();
        
    return Results.Ok(new FilesResponse(files));
});

app.MapGet("/recordings/{filename}", (string filename) =>
{
    var filepath = Path.Combine(recordingsDir, filename);
    if (!File.Exists(filepath))
    {
        return Results.NotFound(new { detail = "File not found" });
    }
    
    return Results.File(filepath, "video/mp4", filename);
});

app.MapDelete("/recordings/{filename}", (string filename) =>
{
    var filepath = Path.Combine(recordingsDir, filename);
    if (!File.Exists(filepath))
    {
        return Results.NotFound(new { detail = "File not found" });
    }
    
    try
    {
        File.Delete(filepath);
        return Results.Ok(new { status = "deleted", filename });
    }
    catch (Exception e)
    {
        return Results.InternalServerError(new { detail = $"Failed to delete file: {e.Message}" });
    }
});

app.Run("http://0.0.0.0:8000");
