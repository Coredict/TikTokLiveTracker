from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.responses import FileResponse
from pydantic import BaseModel
from recorder import recorder
import os

app = FastAPI(title="TikTok Live Recorder API")

RECORDINGS_DIR = "recordings"

@app.get("/")
async def root():
    return {"status": "ok", "message": "TikTok Live Recorder API is running"}

# --- RECORDING SESSIONS ---

@app.get("/record")
async def get_active():
    """List active recording usernames."""
    return {"active_recordings": recorder.get_active_recordings()}

@app.post("/record/{username}")
async def start_record(username: str):
    """Start recording a user."""
    username = username.strip().replace("@", "")
    
    # Check if already recording
    if username in recorder.get_active_recordings():
        return {"status": "already_recording", "username": username}

    # Fetch stream URL
    stream_url = await recorder.get_stream_url(username)
    if not stream_url:
        raise HTTPException(status_code=404, detail=f"User {username} is offline or stream URL not found.")

    # Start recording
    success = recorder.start_recording(username, stream_url)
    if not success:
        raise HTTPException(status_code=500, detail="Failed to start recording process.")

    return {
        "status": "recording_started",
        "username": username
    }

@app.delete("/record/{username}")
async def stop_record(username: str):
    """Stop an active recording."""
    username = username.strip().replace("@", "")
    success = recorder.stop_recording(username)
    if not success:
        raise HTTPException(status_code=404, detail=f"No active recording found for {username}")
    
    return {"status": "recording_stopped", "username": username}

# --- RECORDED FILES ---

@app.get("/recordings")
async def list_files():
    """List saved recorded files."""
    if not os.path.exists(RECORDINGS_DIR):
        return {"files": []}
    
    files = [f for f in os.listdir(RECORDINGS_DIR) if f.endswith(".mp4")]
    return {"files": sorted(files, reverse=True)}

@app.get("/recordings/{filename}")
async def get_file(filename: str):
    """Download a recorded file."""
    filepath = os.path.join(RECORDINGS_DIR, filename)
    if not os.path.exists(filepath):
        raise HTTPException(status_code=404, detail="File not found")
    
    return FileResponse(filepath, media_type="video/mp4", filename=filename)

@app.delete("/recordings/{filename}")
async def delete_file(filename: str):
    """Delete a recorded file."""
    filepath = os.path.join(RECORDINGS_DIR, filename)
    if not os.path.exists(filepath):
        raise HTTPException(status_code=404, detail="File not found")
    
    try:
        os.remove(filepath)
        return {"status": "deleted", "filename": filename}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to delete file: {e}")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8010)
