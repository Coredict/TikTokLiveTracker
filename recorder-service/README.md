This microservice provides an API to record TikTok live streams and save them as `.mp4` video files. It is designed to be run as a Docker container.

## Key Components

- **`recorder.py`**: Handles connection to TikTok, extracts the real-time stream URL (HLS/FLV), and manages `ffmpeg` subprocesses for recording.
- **`main.py`**: A FastAPI application that exposes endpoints to start, stop, and monitor recordings.
- **`Dockerfile`**: Packages the application with Python 3.11 and the `ffmpeg` library.

## How to Run

### 1. Build the Docker Image
Navigate to the `recorder-service` directory and run:
```powershell
docker build -t tiktok-recorder .
```

### 2. Run the Container
Run the container using a **named volume** for persistence. This ensures the recordings are stored by Docker and will only be deleted if you manually remove the volume.

#### Option A: Named Volume (Recommended for Persistence)
```powershell
# Create the volume once
docker volume create tiktok-recordings

# Run the container using the volume
docker run -d `
  -p 8000:8000 `
  -v tiktok-recordings:/app/recordings `
  --name tiktok-recorder `
  tiktok-recorder
```

#### Option B: Bind Mount (Directly to your folder)
```powershell
docker run -d `
  -p 8000:8000 `
  -v ${PWD}/recordings:/app/recordings `
  --name tiktok-recorder `
  tiktok-recorder
```

## API Usage Examples

### Start Recording
`POST /record` with the TikTok username.
```bash
curl -X POST http://localhost:8000/record `
  -H "Content-Type: application/json" `
  -d '{"username": "khaby.lame"}'
```

### Stop Recording
`DELETE /record/{username}`
```bash
curl -X DELETE http://localhost:8000/record/khaby.lame
```

### List Active Recordings
`GET /recordings/active`
```bash
curl http://localhost:8000/recordings/active
```

### List Recorded Files
`GET /recordings/files`
```bash
curl http://localhost:8000/recordings/files
```

## Persistence Notes
The implementation uses asynchronous tasks to avoid blocking the API during recordings. Using a **named volume** is the safest way to ensure your recordings survive container restarts and updates.
