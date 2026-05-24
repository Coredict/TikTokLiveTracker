using CliWrap;

namespace TikTokTracker.Recorder.Services;

/// <summary>
/// Abstraction over ffmpeg process execution so RecordingService can be unit-tested
/// without spawning real ffmpeg processes.
/// </summary>
public interface IFfmpegRunner
{
    /// <summary>
    /// Runs ffmpeg to record a stream. Blocks until the stream ends or the token is cancelled.
    /// Returns (exitCode, stderr) for logging purposes.
    /// </summary>
    Task<(int ExitCode, string StdErr)> RunAsync(string streamUrl, string outputPath, CancellationToken ct);
}

public class FfmpegRunner : IFfmpegRunner
{
    public async Task<(int ExitCode, string StdErr)> RunAsync(string streamUrl, string outputPath, CancellationToken ct)
    {
        var stderr = new System.Text.StringBuilder();
        var result = await Cli.Wrap("ffmpeg")
            .WithArguments(new[]
            {
                "-user_agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                "-i", streamUrl,
                "-c", "copy",
                "-bsf:a", "aac_adtstoasc",
                "-f", "mp4",
                "-movflags", "frag_keyframe+empty_moov+default_base_moof",
                "-y", outputPath
            })
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(ct);

        return (result.ExitCode, stderr.ToString());
    }
}
