import asyncio
import os
import sys
from TikTokLive import TikTokLiveClient
import json

async def test_get_stream_url(username):
    session_id = os.environ.get("TIKTOK_SESSION_ID")
    print(f"Testing with username: {username}")
    print(f"Session ID present: {bool(session_id)}")
    
    client = TikTokLiveClient(unique_id=username)
    if session_id:
        if hasattr(client.web, 'set_session_id'):
            client.web.set_session_id(session_id)
        else:
            client.web.set_session(session_id, None)
            
    try:
        print("Fetching room ID...")
        room_id = await client.web.fetch_room_id_from_api(username)
        print(f"Room ID: {room_id}")
        
        if not room_id:
            print("Could not find room ID.")
            return

        print("Fetching room info...")
        room_info = await client.web.fetch_room_info(room_id)
        
        if not room_info:
            print("Failed to fetch room info.")
            return

        print(f"Room info type: {type(room_info)}")
        
        # Try to find stream URL
        stream_data = None
        if isinstance(room_info, dict):
            stream_data = room_info.get('stream_url')
        else:
            stream_data = getattr(room_info, 'stream_url', None)

        if not stream_data:
            print(f"Could not find stream URL attribute. room_info: {room_info}")
            if hasattr(room_info, '__dict__'):
                print(f"Attributes: {room_info.__dict__.keys()}")
            return

        print(f"Stream data type: {type(stream_data)}")
        print(f"Stream data: {stream_data}")

        if isinstance(stream_data, dict):
            hls_url = stream_data.get('hls_pull_url') or stream_data.get('rtmp_pull_url')
            print(f"HLS URL from dict: {hls_url}")
            
            pull_data = stream_data.get('live_core_sdk_data', {}).get('pull_data', {})
            stream_options = pull_data.get('stream_data', {})
            print(f"Stream options: {stream_options}")
        else:
            hls_url = getattr(stream_data, 'hls_pull_url', None) or getattr(stream_data, 'rtmp_pull_url', None)
            print(f"HLS URL from object: {hls_url}")

    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python test_stream.py <username>")
        sys.exit(1)
    
    username = sys.argv[1].replace("@", "")
    asyncio.run(test_get_stream_url(username))
