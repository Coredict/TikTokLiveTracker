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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _localSessionId;

    public TikTokUrlProvider(ILogger<TikTokUrlProvider> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _localSessionId = configuration["TIKTOK_SESSION_ID"] ?? "";
    }

    private async Task<string> GetActiveSessionIdAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var baseUrl = _configuration["WEB_APP_URL"] ?? "http://app:8080";
            
            _logger.LogInformation("Attempting to fetch session ID from {Url}", $"{baseUrl}/api/config/tiktok-session-id");
            var response = await client.GetAsync($"{baseUrl}/api/config/tiktok-session-id");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("sessionId", out var sessionIdProp))
                {
                    var sessionId = sessionIdProp.GetString();
                    if (!string.IsNullOrWhiteSpace(sessionId))
                    {
                        _logger.LogInformation("Successfully fetched session ID from Web API.");
                        return sessionId;
                    }
                }
            }
            
            _logger.LogWarning("Web API returned unsuccessful status code: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to fetch session ID from Web API: {Message}", ex.Message);
        }

        _logger.LogInformation("Using local fallback session ID.");
        return _localSessionId;
    }

    public async Task<string?> GetStreamUrlAsync(string username)
    {
        string? cookieFile = null;
        try
        {
            var url = $"https://www.tiktok.com/@{username}/live";
            var arguments = new List<string> 
            { 
                "-g", 
                "--impersonate", "chrome",
                url 
            };
            
            var activeSessionId = await GetActiveSessionIdAsync();
            
            if (!string.IsNullOrEmpty(activeSessionId))
            {
                // Create a temporary cookie file in Netscape format
                cookieFile = Path.Combine(Path.GetTempPath(), $"tiktok_cookies_{Guid.NewGuid()}.txt");
                var cookieContent = "# Netscape HTTP Cookie File\n" +
                                   $".tiktok.com\tTRUE\t/\tTRUE\t0\tsessionid\t{activeSessionId}\n";
                await File.WriteAllTextAsync(cookieFile, cookieContent);
                
                arguments.Add("--cookies");
                arguments.Add(cookieFile);
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
        finally
        {
            if (cookieFile != null && File.Exists(cookieFile))
            {
                try { File.Delete(cookieFile); } catch { }
            }
        }

        return null;
    }
}
