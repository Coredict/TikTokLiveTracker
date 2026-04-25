# TikTok Live Tracker

TikTok Live Tracker is a .NET 10 app for monitoring TikTok live accounts, capturing gift events in real time, and recording streams to MP4.

## Features

- Real-time live status and gift tracking for configured TikTok accounts.
- Stream recording via `yt-dlp` + `ffmpeg` through a dedicated recorder service.
- Blazor Server dashboard with leaderboard and admin pages.
- Batched write strategy for gift events to reduce database I/O.
- JSON export for recent gifts and top gifters.
- Docker volumes for persistent database, recordings, and app key storage.

## Stack

- `TikTokTracker.Web`: ASP.NET Core / Blazor Server (.NET 10)
- `recorder-service`: ASP.NET Core Web API (.NET 10)
- Database: PostgreSQL 16
- UI: Radzen Blazor
- TikTok events: [TikTokLiveSharp](https://github.com/FrankRabelo/TikTokLiveSharp)

## Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Optional: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for local development outside containers

### Configure `TIKTOK_SESSION_ID`

If you want improved access to age-restricted streams, set `TIKTOK_SESSION_ID` for both `app` and `recorder` in `docker-compose.yml`.

> Security note: never commit a real session ID to source control. Use a local-only value and rotate it if exposed.

### Run with Docker

```bash
git clone https://github.com/Coredict/TikTokLiveTracker.git
cd TikTokTracker
docker compose up --build
```

### Endpoints

- Dashboard: `http://localhost:5000`
- Admin: `http://localhost:5000/admin`
- Recorder health: `http://localhost:8001/health/live`

## Project Layout

- `TikTokTracker.Web`: UI, monitoring, TikTok event ingestion, and recorder API orchestration
- `recorder-service`: recording API and process management for `yt-dlp`/`ffmpeg`
- `TikTokTracker.Tests`: test project

## Persistence

Docker volumes used by default:

- `pgdata`: PostgreSQL data
- `tiktok-recordings`: recorded MP4 output
- `app-keys`: ASP.NET Core data-protection keys

## License

Licensed under MIT. See `LICENSE`.
