using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Application.Content;

/// <summary>
/// Content hierarchy management, content item management and publication, and resource upload
/// (15.3 "Content delivery"). Every operation first resolves the caller's scope over the owning section
/// (SEC-12): only the assigned instructor or a section administrator may manage; everyone else receives
/// not-found (API-06). Stored files are removed only after the database commit succeeds.
/// </summary>
public sealed class ContentService(
    ICourseContentRepository contents,
    ISectionAccess sections,
    ICurrentUser currentUser,
    SectionScopeResolver scopes,
    IFileStore files,
    IOptions<StorageOptions> storage,
    ILmsUnitOfWork unitOfWork)
{
    // ----- Sections the caller may manage -----

    public async Task<IReadOnlyList<SectionSummary>> ListMySectionsAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? await sections.ListSectionsForInstructorAsync(userId, cancellationToken)
            : [];

    /// <summary>The section's hierarchy as the caller may see it: every item for managers, published items for enrolled learners.</summary>
    public async Task<Result<SectionContentResponse>> GetSectionContentAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveForReadingAsync(sectionId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<SectionContentResponse>(LmsErrors.SectionNotFound);
        }

        var units = await contents.ListBySectionAsync(sectionId, cancellationToken);
        return Result.Success(new SectionContentResponse(
            scope.Section, scope.CanManage, units.Select(c => c.ToResponse(includeUnpublished: scope.CanManage)).ToList()));
    }

    // ----- Content hierarchy management -----

    public async Task<Result<CourseContentResponse>> CreateContentAsync(Guid sectionId, CreateCourseContentRequest request, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveForManagementAsync(sectionId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<CourseContentResponse>(LmsErrors.SectionNotFound);
        }

        var sortOrder = request.SortOrder ?? await NextSortOrderAsync(sectionId, cancellationToken);
        var content = CourseContent.Create(sectionId, request.TitleEn, request.TitleAr, sortOrder);
        contents.Add(content);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(content.ToResponse(includeUnpublished: true));
    }

    public async Task<Result<CourseContentResponse>> GetContentAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var (content, scope) = await LoadForReadingAsync(contentId, cancellationToken);
        return content is null
            ? Result.Failure<CourseContentResponse>(LmsErrors.ContentNotFound)
            : Result.Success(content.ToResponse(includeUnpublished: scope!.CanManage));
    }

    public async Task<Result<CourseContentResponse>> UpdateContentAsync(Guid contentId, UpdateCourseContentRequest request, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content is null)
        {
            return Result.Failure<CourseContentResponse>(LmsErrors.ContentNotFound);
        }

        content.Amend(request.TitleEn, request.TitleAr, request.SortOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(content.ToResponse(includeUnpublished: true));
    }

    /// <summary>Logical deletion of the unit and everything it owns (DC-03); stored files are removed after the commit.</summary>
    public async Task<Result> DeleteContentAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content is null)
        {
            return Result.Failure(LmsErrors.ContentNotFound);
        }

        var storedPaths = content.AllResources.Select(r => r.StoredPath).ToList();
        contents.Remove(content);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteFilesAsync(storedPaths, cancellationToken);
        return Result.Success();
    }

    // ----- Content items and publication -----

    public async Task<Result<ContentItemResponse>> AddItemAsync(Guid contentId, CreateContentItemRequest request, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content is null)
        {
            return Result.Failure<ContentItemResponse>(LmsErrors.ContentNotFound);
        }

        var item = content.AddItem(request.TitleEn, request.TitleAr, request.ItemType, request.Body, request.SortOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(item.ToResponse());
    }

    public async Task<Result<ContentItemResponse>> UpdateItemAsync(Guid contentId, Guid itemId, UpdateContentItemRequest request, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content?.FindItemOrDefault(itemId) is null)
        {
            return Result.Failure<ContentItemResponse>(LmsErrors.ContentItemNotFound);
        }

        var item = content.AmendItem(itemId, request.TitleEn, request.TitleAr, request.Body, request.SortOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(item.ToResponse());
    }

    public async Task<Result> RemoveItemAsync(Guid contentId, Guid itemId, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content?.FindItemOrDefault(itemId) is null)
        {
            return Result.Failure(LmsErrors.ContentItemNotFound);
        }

        var removed = content.RemoveItem(itemId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteFilesAsync(removed.Select(r => r.StoredPath), cancellationToken);
        return Result.Success();
    }

    public Task<Result<ContentItemResponse>> PublishItemAsync(Guid contentId, Guid itemId, CancellationToken cancellationToken) =>
        TransitionItemAsync(contentId, itemId, c => c.PublishItem(itemId), cancellationToken);

    public Task<Result<ContentItemResponse>> UnpublishItemAsync(Guid contentId, Guid itemId, CancellationToken cancellationToken) =>
        TransitionItemAsync(contentId, itemId, c => c.UnpublishItem(itemId), cancellationToken);

    public async Task<Result<CourseContentResponse>> ReorderItemsAsync(Guid contentId, ReorderItemsRequest request, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content is null)
        {
            return Result.Failure<CourseContentResponse>(LmsErrors.ContentNotFound);
        }

        content.ReorderItems(request.ItemIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(content.ToResponse(includeUnpublished: true));
    }

    // ----- Resource upload (SEC-22, SEC-23, SEC-26) -----

    public async Task<Result<ResourceResponse>> UploadResourceAsync(Guid contentId, Guid itemId, FileUpload upload, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content?.FindItemOrDefault(itemId) is null)
        {
            return Result.Failure<ResourceResponse>(LmsErrors.ContentItemNotFound);
        }

        var options = storage.Value;
        var extension = Path.GetExtension(upload.FileName);
        if (string.IsNullOrEmpty(extension) || !options.IsPermittedExtension(extension))
        {
            return Result.Failure<ResourceResponse>(LmsErrors.UploadExtensionNotPermitted(options.PermittedExtensions));
        }

        if (upload.Length <= 0)
        {
            return Result.Failure<ResourceResponse>(LmsErrors.UploadEmpty);
        }

        if (upload.Length > options.MaxUploadSizeBytes)
        {
            return Result.Failure<ResourceResponse>(LmsErrors.UploadTooLarge(options.MaxUploadSizeBytes));
        }

        // 18.5: layout derived from the owning identifiers; the store generates the file name (SEC-23).
        var directory = $"content/{content.SectionId:N}/{itemId:N}";
        var stored = await files.SaveAsync(directory, extension, upload.Content, options.MaxUploadSizeBytes, cancellationToken);
        if (stored is null)
        {
            // The declared length was within bounds but the stream was not (or was empty): nothing was kept.
            return Result.Failure<ResourceResponse>(upload.Length > 0 ? LmsErrors.UploadTooLarge(options.MaxUploadSizeBytes) : LmsErrors.UploadEmpty);
        }

        try
        {
            var resource = content.AttachResource(itemId, upload.FileName, stored.RelativePath, upload.ContentType, stored.SizeBytes, stored.ContentHash);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(resource.ToResponse());
        }
        catch
        {
            // No orphaned files: the record was not committed, so the stored file goes too.
            await files.DeleteAsync(stored.RelativePath, CancellationToken.None);
            throw;
        }
    }

    public async Task<Result> RemoveResourceAsync(Guid contentId, Guid itemId, Guid resourceId, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content?.FindItemOrDefault(itemId)?.FindResourceOrDefault(resourceId) is null)
        {
            return Result.Failure(LmsErrors.ResourceNotFound);
        }

        var resource = content.DetachResource(itemId, resourceId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(resource.StoredPath, cancellationToken);
        return Result.Success();
    }

    // ----- Helpers -----

    private async Task<Result<ContentItemResponse>> TransitionItemAsync(Guid contentId, Guid itemId, Func<CourseContent, ContentItem> transition, CancellationToken cancellationToken)
    {
        var content = await LoadForManagementAsync(contentId, cancellationToken);
        if (content?.FindItemOrDefault(itemId) is null)
        {
            return Result.Failure<ContentItemResponse>(LmsErrors.ContentItemNotFound);
        }

        var item = transition(content);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(item.ToResponse());
    }

    /// <summary>The aggregate when it exists and the caller manages its section; otherwise null (API-06).</summary>
    private async Task<CourseContent?> LoadForManagementAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var content = await contents.FindByIdAsync(contentId, cancellationToken);
        if (content is null)
        {
            return null;
        }

        return await scopes.ResolveForManagementAsync(content.SectionId, cancellationToken) is null ? null : content;
    }

    private async Task<(CourseContent? Content, SectionScope? Scope)> LoadForReadingAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var content = await contents.FindByIdAsync(contentId, cancellationToken);
        if (content is null)
        {
            return (null, null);
        }

        var scope = await scopes.ResolveForReadingAsync(content.SectionId, cancellationToken);
        return scope is null ? (null, null) : (content, scope);
    }

    private async Task<int> NextSortOrderAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var existing = await contents.ListBySectionAsync(sectionId, cancellationToken);
        return existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
    }

    private async Task DeleteFilesAsync(IEnumerable<string> storedPaths, CancellationToken cancellationToken)
    {
        foreach (var path in storedPaths)
        {
            await files.DeleteAsync(path, cancellationToken);
        }
    }
}
