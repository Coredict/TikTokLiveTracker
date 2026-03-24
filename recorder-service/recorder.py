import asyncio
import os
import subprocess
import signal
import logging
from datetime import datetime
from TikTokLive import TikTokLiveClient
try:
    from TikTokLive.client.errors import LiveNotFound, UserOffline, AgeRestrictedError
except ImportError:
    try:
        from TikTokLive.errors import LiveNotFound, UserOffline, AgeRestrictedError
    except ImportError:
        class LiveNotFound(Exception): pass
        class UserOffline(Exception): pass
        class AgeRestrictedError(Exception): pass

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

RECORDINGS_DIR = "recordings"
os.makedirs(RECORDINGS_DIR, exist_ok=True)

class TikTokRecorder:
    def __init__(self):
        self.active_recordings: dict[str, dict] = {}

    async def get_stream_url(self, username: str):
        """Fetch the stream URL for a given TikTok username."""
        session_id = os.environ.get("TIKTOK_SESSION_ID")
        
        # --- Method 1: yt-dlp (Robust) ---
        try:
            import yt_dlp
            headers = {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36',
            }
            if session_id:
                headers['Cookie'] = f'sessionid={session_id}'

            ydl_opts = {
                'quiet': True,
                'no_warnings': True,
                'http_headers': headers
            }
            
            loop = asyncio.get_event_loop()
            def extract_info():
                with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                    return ydl.extract_info(f"https://www.tiktok.com/@{username}/live", download=False)
            
            info = await loop.run_in_executor(None, extract_info)
            if info and 'url' in info:
                logger.info(f"Found URL for {username} via yt-dlp: {info.get('url')[:50]}...")
                return info['url']
        except Exception as e:
            logger.warning(f"yt-dlp failed to get stream URL for {username}: {e}")

        # --- Method 2: TikTokLive (Fallback) ---
        logger.info(f"Falling back to TikTokLive for {username}")
        client = TikTokLiveClient(unique_id=username)
        if session_id:
            if hasattr(client.web, 'set_session'):
                client.web.set_session(session_id, None)
            elif hasattr(client.web, 'set_session_id'):
                client.web.set_session_id(session_id)
        
        try:
            room_id = await client.web.fetch_room_id_from_api(username)
            if not room_id:
                return None
                
            room_info = await client.web.fetch_room_info(room_id)
            if not room_info:
                return None

            stream_data = getattr(room_info, 'stream_url', None) or (room_info.get('stream_url') if isinstance(room_info, dict) else None)
            if not stream_data:
                return None
            
            if isinstance(stream_data, dict):
                return stream_data.get('hls_pull_url') or stream_data.get('rtmp_pull_url')
            else:
                return getattr(stream_data, 'hls_pull_url', None) or getattr(stream_data, 'rtmp_pull_url', None)

        except Exception as e:
            logger.error(f"TikTokLive fallback failed for {username}: {e}")
            return None

    def start_recording(self, username: str, stream_url: str):
        """Start an ffmpeg process to record the stream."""
        if username in self.active_recordings:
            logger.warning(f"Recording already active for {username}")
            return False

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"{username}_{timestamp}.mp4"
        filepath = os.path.join(RECORDINGS_DIR, filename)

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
            if hasattr(os, 'killpg'):
                os.killpg(os.getpgid(process.pid), signal.SIGINT)
            else:
                sig = getattr(signal, 'CTRL_C_EVENT', signal.SIGINT)
                process.send_signal(sig)
            
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
        to_remove = []
        for username, data in self.active_recordings.items():
            if data["process"].poll() is not None:
                to_remove.append(username)
        
        for username in to_remove:
            logger.info(f"Process for {username} finished independently.")
            del self.active_recordings[username]

        return sorted(list(self.active_recordings.keys()))

recorder = TikTokRecorder()
