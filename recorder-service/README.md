# TikTok Live Recorder (C# .NET 10)

This microservice provides an API to record TikTok live streams and save them as `.mp4` video files. It is a modernized C# .NET 10 implementation of the original Python recorder.

## Key Components

- **`Program.cs`**: ASP.NET Core Minimal API exposing the recording endpoints.
- **`Services/TikTokUrlProvider.cs`**: Extracts the real-time stream URL using `yt-dlp`.
- **`Services/RecordingService.cs`**: Manages asynchronous `ffmpeg` subprocesses for recording.
- **`Dockerfile`**: Packages the application with the .NET 10 runtime, `ffmpeg`, and `yt-dlp`.

## How to Run

### 1. Build and Run with Docker Compose
From the project root:
```powershell
docker-compose up --build -d recorder
```

### 2. Run Locally
Navigate to the `recorder-service` directory:
```powershell
dotnet run
```
*Note: Requires `ffmpeg` and `yt-dlp` to be installed and available in your PATH.*

## API Endpoints

### Recording Management
- `GET /record`: List active recording usernames.
- `POST /record/{username}`: Start recording a user.
- `DELETE /record/{username}`: Stop an active recording.

### File Management
- `GET /recordings`: List saved recorded files.
- `GET /recordings/{filename}`: Download a recorded file.
- `DELETE /recordings/{filename}`: Delete a recorded file.

## Environment Variables
- `TIKTOK_SESSION_ID`: (Optional) Provide a valid TikTok session ID to access age-restricted or private streams.
