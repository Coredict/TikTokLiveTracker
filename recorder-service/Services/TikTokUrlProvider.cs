using CliWrap;
using CliWrap.Buffered;
using System.Text.Json;

namespace TikTokTracker.Recorder.Services;

public interface ITikTokUrlProvider
{
    Task<string?> GetStreamUrlAsync(string username);
}

public class TikTokUrlProvider : ITikTokUrlProvider
{
    private readonly ILogger<TikTokUrlProvider> _logger;
    private readonly string _sessionId;

    public TikTokUrlProvider(ILogger<TikTokUrlProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _sessionId = configuration["TIKTOK_SESSION_ID"] ?? "";
    }

    public async Task<string?> GetStreamUrlAsync(string username)
    {
        try
        {
            var url = $"https://www.tiktok.com/@{username}/live";
            var arguments = new List<string> { "-g", url };
            
            if (!string.IsNullOrEmpty(_sessionId))
            {
                arguments.Add("--add-header");
                arguments.Add($"Cookie:sessionid={_sessionId}");
            }

            _logger.LogInformation("Fetching stream URL for {Username} via yt-dlp", username);

            var result = await Cli.Wrap("yt-dlp")
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (result.ExitCode == 0)
            {
                var streamUrl = result.StandardOutput.Trim();
                if (!string.IsNullOrEmpty(streamUrl))
                {
                    _logger.LogInformation("Found stream URL for {Username}", username);
                    return streamUrl;
                }
            }

            _logger.LogWarning("yt-dlp failed to get stream URL for {Username}: {Error}", username, result.StandardError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching stream URL for {Username}", username);
        }

        return null;
    }
}
