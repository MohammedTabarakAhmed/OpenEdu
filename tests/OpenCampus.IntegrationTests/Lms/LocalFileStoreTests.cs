using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Infrastructure.Storage;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>SDD 18.5 and SEC-22/23/26 at the storage layer. File-system based; lives here because UnitTests may not reference Infrastructure (SDD 12).</summary>
public sealed class LocalFileStoreTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "opencampus-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStore _store;

    public LocalFileStoreTests()
    {
        var options = Options.Create(new StorageOptions { RootPath = "data/files", MaxUploadSizeBytes = 1024, PermittedExtensions = [".pdf"] });
        _store = new LocalFileStore(options, _contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Save_GeneratesNameUnderOwnerDirectory_AndRecordsSizeAndSha256()
    {
        var sectionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var directory = $"content/{sectionId:N}/{itemId:N}";

        var stored = await _store.SaveAsync(directory, ".PDF", Bytes("hello"), maxSizeBytes: 1024, CancellationToken.None);

        stored.ShouldNotBeNull();
        // 18.5: deterministic hierarchy derived from the owning identifiers; SEC-23: system-generated file name.
        stored.RelativePath.ShouldStartWith(directory + "/");
        Path.GetFileName(stored.RelativePath).ShouldMatch("^[0-9a-f]{32}\\.pdf$");
        stored.SizeBytes.ShouldBe(5);
        stored.ContentHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData("hello"u8.ToArray()))); // SEC-26
        File.Exists(Path.Combine(_store.RootPath, stored.RelativePath)).ShouldBeTrue();
        _store.RootPath.ShouldBe(Path.GetFullPath(Path.Combine(_contentRoot, "data/files")));
    }

    [Fact]
    public async Task Save_TwoUploadsOfTheSameContent_GetDifferentNames()
    {
        var a = await _store.SaveAsync("x", ".pdf", Bytes("same"), 1024, CancellationToken.None);
        var b = await _store.SaveAsync("x", ".pdf", Bytes("same"), 1024, CancellationToken.None);

        a!.RelativePath.ShouldNotBe(b!.RelativePath);
        a.ContentHash.ShouldBe(b.ContentHash);
    }

    [Fact]
    public async Task Save_BeyondSizeLimit_KeepsNothing()
    {
        var stored = await _store.SaveAsync("x", ".pdf", new MemoryStream(new byte[2048]), maxSizeBytes: 1024, CancellationToken.None);

        stored.ShouldBeNull(); // SEC-22 at application level
        var directory = Path.Combine(_store.RootPath, "x");
        (Directory.Exists(directory) ? Directory.GetFiles(directory) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_EmptyContent_KeepsNothing()
    {
        var stored = await _store.SaveAsync("x", ".pdf", new MemoryStream(), 1024, CancellationToken.None);

        stored.ShouldBeNull();
    }

    [Fact]
    public async Task OpenRead_ReturnsContent_AndNullWhenAbsent()
    {
        var stored = await _store.SaveAsync("x", ".pdf", Bytes("payload"), 1024, CancellationToken.None);

        await using var stream = await _store.OpenReadAsync(stored!.RelativePath, CancellationToken.None);
        stream.ShouldNotBeNull();
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).ShouldBe("payload");

        (await _store.OpenReadAsync("x/missing.pdf", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Delete_RemovesFile_AndToleratesAbsence()
    {
        var stored = await _store.SaveAsync("x", ".pdf", Bytes("bye"), 1024, CancellationToken.None);

        await _store.DeleteAsync(stored!.RelativePath, CancellationToken.None);
        (await _store.OpenReadAsync(stored.RelativePath, CancellationToken.None)).ShouldBeNull();
        await Should.NotThrowAsync(() => _store.DeleteAsync(stored.RelativePath, CancellationToken.None));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("x/../../outside.pdf")]
    [InlineData("C:/Windows/system.ini")]
    [InlineData("")]
    public async Task Paths_OutsideTheRoot_AreRefused(string relativePath)
    {
        await Should.ThrowAsync<ArgumentException>(() => _store.OpenReadAsync(relativePath, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.DeleteAsync(relativePath, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _store.SaveAsync(relativePath, ".pdf", Bytes("x"), 1024, CancellationToken.None));
    }

    [Fact]
    public void Options_ValidateAllowListShape()
    {
        new StorageOptions { RootPath = "data/files", MaxUploadSizeBytes = 1, PermittedExtensions = [".pdf", ".PNG"] }.Validate().ShouldBeTrue();
        new StorageOptions { RootPath = "", MaxUploadSizeBytes = 1, PermittedExtensions = [".pdf"] }.Validate().ShouldBeFalse();
        new StorageOptions { RootPath = "d", MaxUploadSizeBytes = 0, PermittedExtensions = [".pdf"] }.Validate().ShouldBeFalse();
        new StorageOptions { RootPath = "d", MaxUploadSizeBytes = 1, PermittedExtensions = [] }.Validate().ShouldBeFalse();
        new StorageOptions { RootPath = "d", MaxUploadSizeBytes = 1, PermittedExtensions = ["pdf"] }.Validate().ShouldBeFalse();
        new StorageOptions { RootPath = "d", MaxUploadSizeBytes = 1, PermittedExtensions = [".tar.gz"] }.Validate().ShouldBeFalse();

        var options = new StorageOptions { PermittedExtensions = [".pdf"] };
        options.IsPermittedExtension(".PDF").ShouldBeTrue();
        options.IsPermittedExtension(".exe").ShouldBeFalse();
    }
}
