import asyncio
import os
import subprocess
import signal
import logging
from datetime import datetime
from TikTokLive import TikTokLiveClient
from TikTokLive.errors import LiveNotFound, UserOffline

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

RECORDINGS_DIR = "recordings"
os.makedirs(RECORDINGS_DIR, exist_ok=True)

class TikTokRecorder:
    def __init__(self):
        self.active_recordings = {}

    async def get_stream_url(self, username: str):
        """Fetch the stream URL for a given TikTok username."""
        client = TikTokLiveClient(unique_id=username)
        try:
            # We don't need to connect indefinitely, just fetch room info
            room_info = await client.fetch_room_info()
            
            if not room_info or not hasattr(room_info, 'stream_url') or not room_info.stream_url:
                logger.error(f"Could not find stream URL for {username}")
                return None

            stream_data = room_info.stream_url
            
            # In v6, stream_url might be a dict-like or have specific attributes
            # We'll try to get the HLS or FLV URL
            hls_url = stream_data.get('hls_pull_url') or stream_data.get('rtmp_pull_url')
            if hls_url:
                return hls_url
                
            # Try to parse the complex structure if direct pull URLs are missing
            pull_data = stream_data.get('live_core_sdk_data', {}).get('pull_data', {})
            stream_options = pull_data.get('stream_data', {})
            
            import json
            try:
                if isinstance(stream_options, str):
                    stream_options = json.loads(stream_options)
                
                data = stream_options.get('data', {})
                for quality, formats in data.items():
                    main_urls = formats.get('main', {})
                    if main_urls.get('hls'): return main_urls.get('hls')
                    if main_urls.get('flv'): return main_urls.get('flv')
            except:
                pass

            return None

        except (LiveNotFound, UserOffline):
            logger.info(f"User {username} is offline or doesn't exist.")
            return None
        except Exception as e:
            logger.exception(f"Error fetching room info for {username}: {e}")
            return None

    def start_recording(self, username: str, stream_url: str):
        """Start an ffmpeg process to record the stream."""
        if username in self.active_recordings:
            logger.warning(f"Recording already active for {username}")
            return False

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"{username}_{timestamp}.mp4"
        filepath = os.path.join(RECORDINGS_DIR, filename)

        # FFmpeg command to record from URL to MP4
        # -i: Input URL
        # -c copy: Copy codecs (no re-encoding, fast and low CPU)
        # -f mp4: Force MP4 format
        # -y: Overwrite output file if exists
        command = [
            "ffmpeg",
            "-i", stream_url,
            "-c", "copy",
            "-f", "mp4",
            "-y",
            filepath
        ]

        try:
            process = subprocess.Popen(
                command,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                preexec_fn=os.setsid if hasattr(os, 'setsid') else None
            )
            self.active_recordings[username] = {
                "process": process,
                "filename": filename,
                "start_time": datetime.now().isoformat(),
                "filepath": filepath
            }
            logger.info(f"Started recording for {username} -> {filename}")
            return True
        except Exception as e:
            logger.exception(f"Failed to start ffmpeg for {username}: {e}")
            return False

    def stop_recording(self, username: str):
        """Stop the ffmpeg process for a given username."""
        recording = self.active_recordings.get(username)
        if not recording:
            return False

        process = recording["process"]
        try:
            # Try to stop gracefully with 'q' command to FFmpeg vs killing it
            # But process.send_signal(signal.SIGINT) is usually enough for FFmpeg to finalize the MP4
            if hasattr(os, 'killpg'):
                os.killpg(os.getpgid(process.pid), signal.SIGINT)
            else:
                process.send_signal(signal.CTRL_C_EVENT if hasattr(signal, 'CTRL_C_EVENT') else signal.SIGINT)
            
            # Wait for process to exit
            process.wait(timeout=10)
            logger.info(f"Stopped recording for {username}")
        except Exception as e:
            logger.warning(f"Error stopping ffmpeg for {username}: {e}. Killing process.")
            process.kill()
        finally:
            del self.active_recordings[username]
            return True

    def get_active_recordings(self):
        """Return a list of active recording usernames."""
        # Clean up finished processes
        to_remove = []
        for username, data in self.active_recordings.items():
            if data["process"].poll() is not None:
                to_remove.append(username)
        
        for username in to_remove:
            logger.info(f"Process for {username} finished independently.")
            del self.active_recordings[username]

        return list(self.active_recordings.keys())

recorder = TikTokRecorder()
