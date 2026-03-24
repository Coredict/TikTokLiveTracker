using Microsoft.AspNetCore.Mvc;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ITikTokUrlProvider, TikTokUrlProvider>();
builder.Services.AddSingleton<IRecordingService, RecordingService>();
builder.Services.AddControllers();

var app = builder.Build();

var recordingsDir = "recordings";
if (!Directory.Exists(recordingsDir))
{
    Directory.CreateDirectory(recordingsDir);
}

app.MapControllers();

app.Run("http://0.0.0.0:8000");
