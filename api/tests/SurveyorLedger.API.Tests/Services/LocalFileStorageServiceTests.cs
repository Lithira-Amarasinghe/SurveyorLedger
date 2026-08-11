using System.Text;
using Microsoft.Extensions.Configuration;
using SurveyorLedger.API.Services;
using Xunit;

namespace SurveyorLedger.API.Tests.Services;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sl-storage-test-{Guid.NewGuid():N}");
    private readonly LocalFileStorageService _sut;

    public LocalFileStorageServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:UploadsRootPath"] = _root })
            .Build();
        _sut = new LocalFileStorageService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_WritesFile_UnderConfiguredRoot()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";

        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "workspace1", "job1", "abc_file.pdf")));
    }

    [Fact]
    public async Task OpenAsync_ReturnsSavedContent()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";
        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        await using var stream = await _sut.OpenAsync(relativePath, CancellationToken.None);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        Assert.Equal("hello", text);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var relativePath = "workspace1/job1/abc_file.pdf";
        await _sut.SaveAsync(content, relativePath, CancellationToken.None);

        await _sut.DeleteAsync(relativePath, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, "workspace1", "job1", "abc_file.pdf")));
    }
}
