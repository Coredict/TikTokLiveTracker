using Microsoft.AspNetCore.Mvc;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;
using CliWrap;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ITikTokUrlProvider, TikTokUrlProvider>();
builder.Services.AddSingleton<IRecordingService, RecordingService>();
builder.Services.AddHostedService<RecordingCleanupService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddCheck("dependencies", () =>
    {
        try
        {
            var ffmpegCheck = Cli.Wrap("ffmpeg").WithArguments("-version").ExecuteAsync().Task.Wait(1000);
            var ytdlpCheck = Cli.Wrap("yt-dlp").WithArguments("--version").ExecuteAsync().Task.Wait(1000);

            if (ffmpegCheck && ytdlpCheck)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
            }
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Dependencies (ffmpeg or yt-dlp) not found or timed out.");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Dependency check failed.", ex);
        }
    }, tags: new[] { "ready" });

var app = builder.Build();

var recordingsDir = "recordings";
var tempDir = "tmp";

if (!Directory.Exists(recordingsDir))
{
    Directory.CreateDirectory(recordingsDir);
}

if (!Directory.Exists(tempDir))
{
    Directory.CreateDirectory(tempDir);
}

app.MapControllers();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run("http://0.0.0.0:8001");
