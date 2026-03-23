# TikTok Live Tracker

A high-performance Blazor Server application designed to monitor TikTok live streams, track real-time gift donations, and maintain comprehensive gifter leaderboards. Built with .NET 10, PostgreSQL, and Docker.

![TikTok Tracker Preview](https://via.placeholder.com/800x400?text=TikTok+Live+Tracker+Dashboard) *(Replace with actual screenshot)*

## 🚀 Features

- **Real-time Monitoring**: Track live status and viewer counts for multiple TikTok accounts simultaneously.
- **Gift Tracking**: Automatically captures every gift received, including sender nicknames, usernames, and diamond values.
- **In-Memory Caching**: Implements a high-performance caching layer in the background service, allowing for **1-second UI refreshes** with zero database `SELECT` load.
- **Top Gifters Leaderboard**: Ranked lists of contributors per account with advanced filtering and pagination.
- **Batch Processing**: Individual gift events are buffered and flushed to the database in batches every minute to protect database IO.
- **JSON Export**: Export recent gifts and top gifter data as formatted JSON files directly from the browser.
- **Containerized Recovery**: Uses persistent Docker volumes for PostgreSQL data and ASP.NET Core Data Protection keys (prevents session logout on restart).

## 🛠️ Tech Stack

- **Frontend/Backend**: .NET 10 (ASP.NET Core / Blazor Server)
- **Database**: PostgreSQL 16
- **ORM**: Entity Framework Core (Npgsql)
- **Real-time Data**: [TikTokLiveSharp](https://github.com/FrankRabelo/TikTokLiveSharp) (WebSocket Client)
- **Deployment**: Docker & Docker Compose

## 🏁 Getting Started

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (optional for local development)

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/TikTokTracker.git
   cd TikTokTracker
   ```

2. Start the application using Docker Compose:
   ```bash
   docker compose up --build
   ```

3. Access the application:
   - **Dashboard**: `http://localhost:5000`
   - **Admin Panel**: `http://localhost:5000/admin` (Default: No password required, configurable in `AdminSessionService`).

## 🏗️ Architecture

The application follows a background service architecture:

1. **TikTokTrackerService**: A singleton background worker that:
   - Polls the list of accounts to check live status.
   - Manages WebSocket connections for active live streams.
   - Maintains an in-memory cache for the Blazor UI.
   - Buffers gift events and flushes them to PostgreSQL in batches using high-performance `ON CONFLICT` (Upsert) logic.
2. **Blazor UI**: A reactive dashboard that reads directly from the shared service cache, ensuring minimal latency and database impact.

## 🧹 Maintenance

- **Auto-Cleanup**: The system automatically purges individual gift transactions older than 24 hours to keep the database size manageable while preserving all-time leaderboard statistics.
- **Key Persistence**: Session keys are stored in the `app-keys` volume, ensuring you stay logged in even after container updates.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
