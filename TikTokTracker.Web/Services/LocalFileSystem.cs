namespace TikTokTracker.Web.Services;

public class LocalFileSystem : IFileSystem
{
    public bool Exists(string path) => File.Exists(path);
    public Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public Task WriteAllTextAsync(string path, string contents) => File.WriteAllTextAsync(path, contents);
}
