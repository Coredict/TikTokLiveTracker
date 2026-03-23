from fastapi import FastAPI, BackgroundTasks, HTTPException
from pydantic import BaseModel
from recorder import recorder
import os

app = FastAPI(title="TikTok Live Recorder API")

class RecordRequest(BaseModel):
    username: str

@app.get("/")
async def root():
    return {"status": "ok", "message": "TikTok Live Recorder API is running"}

@app.post("/record")
async def start_record(request: RecordRequest, background_tasks: BackgroundTasks):
    username = request.username.strip().replace("@", "")
    
    # 1. Check if already recording
    if username in recorder.get_active_recordings():
        return {"status": "already_recording", "username": username}

    # 2. Fetch stream URL
    stream_url = await recorder.get_stream_url(username)
    if not stream_url:
        raise HTTPException(status_code=404, detail=f"User {username} is offline or stream URL not found.")

    # 3. Start recording
    success = recorder.start_recording(username, stream_url)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to start recording process.")

    return {
        "status": "recording_started",
        "username": username,
        "stream_url": stream_url[:50] + "..." # Truncated for security/cleanliness
    }

@app.delete("/record/{username}")
async def stop_record(username: str):
    username = username.strip().replace("@", "")
    success = recorder.stop_recording(username)
    if not success:
        raise HTTPException(status_code=404, detail=f"No active recording found for {username}")
    
    return {"status": "recording_stopped", "username": username}

@app.get("/recordings/active")
async def get_active():
    return {"active_recordings": recorder.get_active_recordings()}

@app.get("/recordings/files")
async def list_files():
    recordings_dir = "recordings"
    if not os.path.exists(recordings_dir):
        return {"files": []}
    
    files = [f for f in os.listdir(recordings_dir) if f.endswith(".mp4")]
    return {"files": sorted(files, reverse=True)}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
