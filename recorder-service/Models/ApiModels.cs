namespace TikTokTracker.Recorder.Models;

public record ActiveRecordingsResponse(List<string> active_recordings);
public record VideoFileInfo(string Name, long SizeBytes, double? DurationSeconds);
public record FilesResponse(List<VideoFileInfo> files);
