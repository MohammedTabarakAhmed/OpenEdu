using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Application.Content;

/// <summary>
/// Authorised resource download (15.3; SEC-25): the caller's entitlement to the owning section is verified
/// before any content is streamed. Managers may retrieve every resource; an enrolled learner only those of
/// published items (BR-15). Every other case is not found (API-06), so neither the file nor the item's
/// existence is disclosed.
/// </summary>
public sealed class ResourceService(ICourseContentRepository contents, SectionScopeResolver scopes, IFileStore files)
{
    public async Task<Result<ResourceDownload>> OpenForDownloadAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var content = await contents.FindByResourceIdAsync(resourceId, cancellationToken);
        if (content is null)
        {
            return Result.Failure<ResourceDownload>(LmsErrors.ResourceNotFound);
        }

        var scope = await scopes.ResolveForReadingAsync(content.SectionId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<ResourceDownload>(LmsErrors.ResourceNotFound);
        }

        var item = content.Items.Single(i => i.FindResourceOrDefault(resourceId) is not null);
        if (!scope.CanManage && !item.IsVisibleToLearners)
        {
            // BR-15 for the learner path, reported as absence (API-06).
            return Result.Failure<ResourceDownload>(LmsErrors.ResourceNotFound);
        }

        var resource = item.FindResourceOrDefault(resourceId)!;
        var stream = await files.OpenReadAsync(resource.StoredPath, cancellationToken);
        return stream is null
            ? Result.Failure<ResourceDownload>(LmsErrors.ResourceNotFound)
            : Result.Success(new ResourceDownload(stream, resource.FileName, resource.ContentType, resource.SizeBytes, resource.ContentHash));
    }
}

/// <summary>Learner self-service over content (15.3 "enrolled course listing", "accesses content" of AC-04), scoped to the caller's enrolments (SEC-12).</summary>
public sealed class LearnerContentService(ISectionAccess sections, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<SectionSummary>> ListMySectionsAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? await sections.ListSectionsForLearnerAsync(userId, cancellationToken)
            : [];
}
