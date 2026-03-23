using System.Text;
using System.Text.Json;

namespace TikTokTracker.Web.Services;

public class RecorderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecorderClient> _logger;
    private const string BaseUrl = "http://recorder:8000";

    public RecorderClient(HttpClient httpClient, ILogger<RecorderClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task StartRecordingAsync(string username)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { username = username }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{BaseUrl}/record", content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully triggered recording for @{Username}", username);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to trigger recording for @{Username}. Status: {Status}, Body: {Body}", 
                    username, response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling recorder API for @{Username}", username);
        }
    }

    public async Task StopRecordingAsync(string username)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BaseUrl}/record/{username}");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully stopped recording for @{Username}", username);
            }
            else
            {
                _logger.LogWarning("Failed to stop recording for @{Username}. Status: {Status}", 
                    username, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling recorder API to stop @{Username}", username);
        }
    }
}
