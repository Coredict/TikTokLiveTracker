# TikTok Live Tracker

A high-performance Blazor Server application designed to monitor TikTok live streams, track real-time gift donations, and automatically record live video. Built with .NET 10, Python, PostgreSQL, and Docker.

![TikTok Tracker Preview](https://via.placeholder.com/800x400?text=TikTok+Live+Tracker+Dashboard) *(Replace with actual screenshot)*

## 🚀 Features

- **Real-time Monitoring**: Track live status and viewer counts for multiple TikTok accounts simultaneously.
- **Live Stream Recording**: Automatically or manually record live streams to MP4 files using `ffmpeg` and `yt-dlp`.
- **Gift Tracking**: Automatically captures every gift received, including sender nicknames, usernames, and diamond values.
- **In-Memory Caching**: Implements a high-performance caching layer in the background service, allowing for **1-second UI refreshes** with zero database `SELECT` load.
- **Top Gifters Leaderboard**: Ranked lists of contributors per account with advanced filtering and pagination.
- **Batch Processing**: Individual gift events are buffered and flushed to the database in batches every minute to protect database IO.
- **JSON Export**: Export recent gifts and top gifter data as formatted JSON files directly from the browser.
- **Containerized Recovery**: Uses persistent Docker volumes for PostgreSQL data, recorded videos, and ASP.NET Core Data Protection keys.

## 🛠️ Tech Stack

### Web Interface & Monitoring
- **Frontend/Backend**: .NET 10 (ASP.NET Core / Blazor Server)
- **Database**: PostgreSQL 16
- **Real-time Events**: [TikTokLiveSharp](https://github.com/FrankRabelo/TikTokLiveSharp)
- **UI Components**: Radzen Blazor

### Recording Service
- **Framework**: Python 3.12 (FastAPI)
- **Stream Extraction**: [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- **Video Processing**: FFmpeg
- **Client**: [TikTokLive (Python)](https://github.com/isaackogan/TikTokLive)

## 🏁 Getting Started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (optional for local development)

### Configuration

The recorder service supports age-restricted streams if a valid TikTok session ID is provided.

1. Rename `.env.example` to `.env` (if available) or set environment variables in `docker-compose.yml`.
2. **TIKTOK_SESSION_ID**: Your browser's session ID from tiktok.com (found in cookies).

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/pasti1/TikTokTracker.git
   cd TikTokTracker
   ```

2. Start the services using Docker Compose:
   ```bash
   docker compose up --build
   ```

3. Access the application:
   - **Dashboard**: `http://localhost:5000`
   - **Admin Panel**: `http://localhost:5000/admin`
   - **Recorder API**: `http://localhost:8010/docs` (Swagger UI)

## 🏗️ Architecture

The application is split into two primary services:

1. **TikTokTracker.Web (.NET)**:
   - Manages the UI and monitoring logic.
   - Connects to TikTok via WebSockets for real-time gift events.
   - Communicates with the `recorder-service` via a REST client to trigger/stop recordings.

2. **Recorder Service (Python)**:
   - A lightweight FastAPI service dedicated to video recording.
   - Uses `yt-dlp` to extract the best possible HLS/RTMP stream URL.
   - Spawns `ffmpeg` subprocesses for robust, low-overhead recording to disk.

## 🧹 Maintenance

- **Auto-Cleanup**: The system purges individual gift transactions older than 24 hours to keep the database size manageable.
- **Persistence**: 
  - `pgdata`: Database storage.
  - `tiktok-recordings`: Recorded MP4 files.
  - `app-keys`: Auth tokens and session persistence.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
