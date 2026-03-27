using System.Text.Json;
using System.Text.Json.Serialization;

namespace TikTokTracker.Web.Services;

public class RecorderClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RecorderClient> _logger;
    private readonly string _baseUrl;

    public RecorderClient(HttpClient httpClient, ILogger<RecorderClient> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["Recorder:BaseUrl"] ?? "http://recorder:8001";
    }

    public async Task StartRecordingAsync(string username)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/record/{username}", null);
            
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
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/record/{username}");
            
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

    public async Task<List<string>> GetActiveRecordingsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/record");
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ActiveRecordingsResponse>(body, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result?.ActiveRecordings ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching active recordings");
        }
        return new List<string>();
    }

    public async Task<List<VideoFileInfo>> ListRecordingsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/recordings");
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<FilesResponse>(body, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result?.Files ?? new List<VideoFileInfo>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing recording files");
        }
        return new List<VideoFileInfo>();
    }

    public async Task<bool> DeleteRecordingAsync(string filename)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/recordings/{filename}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting recording: {Filename}", filename);
            return false;
        }
    }

    public async Task<Stream?> GetDownloadStreamAsync(string filename)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/recordings/{filename}", HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming recording: {Filename}", filename);
        }
        return null;
    }

    public string GetDownloadUrl(string filename) => $"/api/recordings/{filename}";

    private class ActiveRecordingsResponse { [JsonPropertyName("active_recordings")] public List<string> ActiveRecordings { get; set; } = new(); }
    private class FilesResponse { public List<VideoFileInfo> Files { get; set; } = new(); }
}

public record VideoFileInfo(string Name, long SizeBytes, double? DurationSeconds);
