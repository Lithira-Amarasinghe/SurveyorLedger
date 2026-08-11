namespace SurveyorLedger.API.Services;

public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct);
    Task<Stream> OpenAsync(string relativePath, CancellationToken ct);
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
