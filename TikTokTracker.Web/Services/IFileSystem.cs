namespace TikTokTracker.Web.Services;

public interface IFileSystem
{
    bool Exists(string path);
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string contents);
}
