namespace SurveyorLedger.API.Services;

/// <summary>
/// Local-disk implementation of IFileStorageService, for dev. relativePath is always
/// {workspaceId}/{jobId}/{guid}_{filename} - callers own that shape, this class just
/// resolves it under the configured root and creates directories as needed. Swapping to
/// Azure Blob later means adding a sibling class implementing the same interface and
/// flipping the DI registration in Program.cs - no caller changes.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _rootPath = configuration["Storage:UploadsRootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "uploads");
    }

    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public Task<Stream> OpenAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        var fullPath = ResolvePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string ResolvePath(string relativePath) => Path.Combine(_rootPath, relativePath);
}
