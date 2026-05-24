using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using TikTokTracker.Recorder.Models;
using TikTokTracker.Recorder.Services;
using Xunit;

namespace TikTokTracker.Recorder.Tests;

/// <summary>
/// Integration tests for the file-based recording endpoints:
/// GET /recordings, GET /recordings/{filename}, DELETE /recordings/{filename}.
///
/// These tests create real files in a "recordings/" directory (relative to the test
/// working directory) because the controller uses the filesystem directly.
/// ffprobe is not available in the test environment, so duration will be null.
/// </summary>
[Collection("FileEndpointTests")]
public class RecordingFileEndpointTests : IClassFixture<RecordingFileEndpointTests.FileTestWebAppFactory>, IDisposable
{
    private readonly FileTestWebAppFactory _factory;
    private readonly string _recordingsDir = "recordings";

    public RecordingFileEndpointTests(FileTestWebAppFactory factory)
    {
        _factory = factory;

        // Ensure a clean recordings directory for each test
        if (Directory.Exists(_recordingsDir))
        {
            foreach (var file in Directory.GetFiles(_recordingsDir))
            {
                File.Delete(file);
            }
        }
        else
        {
            Directory.CreateDirectory(_recordingsDir);
        }
    }

    public void Dispose()
    {
        // Clean up test files after each test
        if (Directory.Exists(_recordingsDir))
        {
            foreach (var file in Directory.GetFiles(_recordingsDir))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private void CreateTestFile(string filename, int sizeBytes = 1024)
    {
        var path = Path.Combine(_recordingsDir, filename);
        File.WriteAllBytes(path, new byte[sizeBytes]);
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    // --- GET /recordings ---

    [Fact]
    public async Task GetRecordedFiles_ShouldReturnEmptyList_WhenNoFiles()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/recordings");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(0, files.GetArrayLength());
    }

    [Fact]
    public async Task GetRecordedFiles_ShouldReturnMp4Files()
    {
        CreateTestFile("user1_20260324_120000.mp4", 2048);
        CreateTestFile("user2_20260324_130000.mp4", 4096);
        // Non-mp4 file should be ignored
        File.WriteAllText(Path.Combine(_recordingsDir, "readme.txt"), "not a video");

        using var client = CreateClient();
        var response = await client.GetAsync("/recordings");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var files = doc.RootElement.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
    }

    [Fact]
    public async Task GetRecordedFiles_ShouldReturnFilesOrderedDescending()
    {
        CreateTestFile("aaa_20260101_000000.mp4");
        CreateTestFile("zzz_20261231_235959.mp4");

        using var client = CreateClient();
        var response = await client.GetAsync("/recordings");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var files = doc.RootElement.GetProperty("files");
        var firstFileName = files[0].GetProperty("name").GetString();
        Assert.Equal("zzz_20261231_235959.mp4", firstFileName);
    }

    [Fact]
    public async Task GetRecordedFiles_ShouldIncludeFileSize()
    {
        CreateTestFile("sized_20260324_120000.mp4", 8192);

        using var client = CreateClient();
        var response = await client.GetAsync("/recordings");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var file = doc.RootElement.GetProperty("files")[0];
        var sizeBytes = file.GetProperty("sizeBytes").GetInt64();
        Assert.Equal(8192, sizeBytes);
    }

    // --- GET /recordings/{filename} ---

    [Fact]
    public async Task DownloadRecording_ShouldReturnFile_WhenExists()
    {
        var testData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        File.WriteAllBytes(Path.Combine(_recordingsDir, "download_test.mp4"), testData);

        using var client = CreateClient();
        var response = await client.GetAsync("/recordings/download_test.mp4");

        response.EnsureSuccessStatusCode();
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(testData, content);
    }

    [Fact]
    public async Task DownloadRecording_ShouldReturnNotFound_WhenFileMissing()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/recordings/nonexistent.mp4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- DELETE /recordings/{filename} ---

    [Fact]
    public async Task DeleteRecording_ShouldDeleteFile_WhenExists()
    {
        CreateTestFile("deleteme.mp4");
        Assert.True(File.Exists(Path.Combine(_recordingsDir, "deleteme.mp4")));

        using var client = CreateClient();
        var response = await client.DeleteAsync("/recordings/deleteme.mp4");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("deleted", content);
        Assert.False(File.Exists(Path.Combine(_recordingsDir, "deleteme.mp4")));
    }

    [Fact]
    public async Task DeleteRecording_ShouldReturnNotFound_WhenFileMissing()
    {
        using var client = CreateClient();
        var response = await client.DeleteAsync("/recordings/ghost.mp4");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- WebApplicationFactory ---

    public class FileTestWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove services we don't need for file endpoint tests
                var typesToRemove = new[]
                {
                    typeof(IRecordingService),
                    typeof(ITikTokUrlProvider),
                    typeof(IFfmpegRunner),
                    typeof(IHostedService)
                };

                var descriptorsToRemove = services
                    .Where(d => typesToRemove.Contains(d.ServiceType))
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // The file endpoints are on RecordingController which also depends on
                // IRecordingService and ITikTokUrlProvider — provide no-op mocks
                services.AddSingleton(new Mock<IRecordingService>().Object);
                services.AddSingleton(new Mock<ITikTokUrlProvider>().Object);
            });
        }
    }
}
