# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["TikTokTracker.Recorder.csproj", "./"]
RUN dotnet restore "TikTokTracker.Recorder.csproj"

# Copy the rest and build
COPY . .
RUN dotnet build "TikTokTracker.Recorder.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "TikTokTracker.Recorder.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y \
    ffmpeg \
    curl \
    ca-certificates \
    python3 \
    python3-pip \
    && rm -rf /var/lib/apt/lists/*

# Install yt-dlp
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

COPY --from=publish /app/publish .

# Create a directory for recordings
RUN mkdir -p /app/recordings

# Make port 8000 available
EXPOSE 8000

# Set environment variables
ENV TIKTOK_SESSION_ID=""

# Run the application
ENTRYPOINT ["dotnet", "TikTokTracker.Recorder.dll"]
