namespace TikTokTracker.Recorder.Models;

public record ActiveRecordingInfo(string username, DateTime started_at);
public record ActiveRecordingsResponse(List<ActiveRecordingInfo> active_recordings);
public record VideoFileInfo(string Name, long SizeBytes, double? DurationSeconds);
public record FilesResponse(List<VideoFileInfo> files);
