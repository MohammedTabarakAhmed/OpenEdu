using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Application.Provisioning;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.SharedKernel;
using static OpenCampus.UnitTests.Lms.Application.LmsHarness;

namespace OpenCampus.UnitTests.Lms.Application;

/// <summary>Upload controls at the application level (SEC-22, SEC-23, SEC-26) and file clean-up on removal.</summary>
public class ResourceUploadTests
{
    [Fact]
    public async Task Upload_StoresUnderOwnerHierarchy_WithGeneratedNameAndHash()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, _) = await h.SeedAsync();

        var result = await h.As(AssignedInstructor).Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("Week 1 Notes.PDF", 512), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBe("Week 1 Notes.PDF"); // SEC-23: display attribute only
        result.Value.SizeBytes.ShouldBe(512);
        result.Value.ContentHash.Length.ShouldBe(64); // SEC-26
        var stored = fileItem.Resources.Single(r => r.Id == result.Value.Id).StoredPath;
        stored.ShouldStartWith($"content/{h.Section.Id:N}/{fileItem.Id:N}/");
        stored.ShouldEndWith(".pdf");
        stored.ShouldNotContain("Week 1 Notes");
        h.Store.Files.ShouldContainKey(stored);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    [InlineData("double.pdf.exe")]
    public async Task Upload_WithExtensionOffTheAllowList_IsRefused_SEC22(string fileName)
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, _) = await h.SeedAsync();
        var before = h.Store.Files.Count;

        var result = await h.As(AssignedInstructor).Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload(fileName, 10), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("File");
        h.Store.Files.Count.ShouldBe(before);
        fileItem.Resources.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Upload_BeyondSizeLimit_IsRefused_SEC22_AndNothingIsKept()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, _) = await h.SeedAsync();
        var before = h.Store.Files.Count;

        // Declared length over the limit.
        var declared = await h.As(AssignedInstructor).Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("big.pdf", 2048), CancellationToken.None);
        declared.Error.Type.ShouldBe(ErrorType.Validation);

        // Declared length within the limit but the stream is not: caught while writing.
        var lying = new FileUpload("big.pdf", "application/pdf", 100, new MemoryStream(new byte[2048]));
        var streamed = await h.Content.UploadResourceAsync(unit.Id, fileItem.Id, lying, CancellationToken.None);
        streamed.Error.Type.ShouldBe(ErrorType.Validation);

        h.Store.Files.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Upload_EmptyFile_IsRefused()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, _) = await h.SeedAsync();

        var result = await h.As(AssignedInstructor).Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("empty.pdf", 0), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Upload_ByNonManager_IsNotFound_AndStoresNothing()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, _) = await h.SeedAsync();
        var before = h.Store.Files.Count;

        foreach (var caller in new[] { EnrolledLearnerId, OtherInstructor })
        {
            var result = await h.As(caller).Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("x.pdf", 10), CancellationToken.None);
            result.Error.Type.ShouldBe(ErrorType.NotFound);
        }

        h.Store.Files.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Upload_ToUnknownItem_IsNotFound()
    {
        var h = new LmsHarness();
        var (unit, _, _, _, _) = await h.SeedAsync();

        var result = await h.As(AssignedInstructor).Content.UploadResourceAsync(unit.Id, Guid.NewGuid(), Upload("x.pdf", 10), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task RemoveResource_RemoveItem_AndDeleteContent_DeleteStoredFilesAfterCommit()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, resource) = await h.SeedAsync();
        h.As(AssignedInstructor);
        var second = (await h.Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("second.pdf", 10), CancellationToken.None)).Value;
        var third = (await h.Content.UploadResourceAsync(unit.Id, fileItem.Id, Upload("third.pdf", 10), CancellationToken.None)).Value;
        h.Store.Files.Count.ShouldBe(3);

        (await h.Content.RemoveResourceAsync(unit.Id, fileItem.Id, resource.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Store.Files.Count.ShouldBe(2);
        h.Store.Files.Keys.ShouldNotContain(resource.StoredPath);

        (await h.Content.RemoveItemAsync(unit.Id, fileItem.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Store.Files.Count.ShouldBe(0);
        _ = second;
        _ = third;

        var other = unit.AddItem("Another", "آخر", ContentItemType.File, null);
        (await h.Content.UploadResourceAsync(unit.Id, other.Id, Upload("again.pdf", 10), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Content.DeleteContentAsync(unit.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Store.Files.Count.ShouldBe(0);
        unit.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Provisioner_SeedsEveryLiveSection_WithAnUnpublishedItem_AndIsIdempotent()
    {
        var h = new LmsHarness();
        h.Access.AddSection(OtherInstructor);
        var provisioner = new LmsProvisioner(h.Repository, h.Access, h.Store, Options.Create(h.Storage),
            Options.Create(new LmsProvisioningOptions { DemonstrationDataEnabled = true }), h.Repository, NullLogger<LmsProvisioner>.Instance);

        await provisioner.RunAsync(CancellationToken.None);
        await provisioner.RunAsync(CancellationToken.None);

        h.Repository.Items.Count.ShouldBe(6); // 2 sections × 3 weeks
        h.Repository.SaveCount.ShouldBe(1);
        h.Store.Files.Count.ShouldBe(2);
        h.Repository.Items.SelectMany(c => c.Items).Count(i => !i.IsPublished).ShouldBe(2);
        h.Repository.Items.SelectMany(c => c.AllResources).Count().ShouldBe(2);
    }

    [Fact]
    public async Task Provisioner_DoesNothingWhenDemonstrationDataIsDisabled()
    {
        var h = new LmsHarness();
        var provisioner = new LmsProvisioner(h.Repository, h.Access, h.Store, Options.Create(h.Storage),
            Options.Create(new LmsProvisioningOptions { DemonstrationDataEnabled = false }), h.Repository, NullLogger<LmsProvisioner>.Instance);

        await provisioner.RunAsync(CancellationToken.None);

        h.Repository.Items.ShouldBeEmpty();
    }
}
