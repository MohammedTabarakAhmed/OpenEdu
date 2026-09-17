using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Application.Assessment;

/// <summary>
/// Assignment publication and listing, submission creation and retrieval, submission marking (15.3 "Assessment
/// and grading"). Scope per section through <see cref="SectionScopeResolver"/> (SEC-12): managers see and mark
/// every submission of their sections; a learner sees published assignments of sections they are enrolled in and
/// only their own submission; anything else is not found (API-06). BR-08 and BR-09 are enforced by the aggregate.
/// </summary>
public sealed class AssignmentService(
    IAssignmentRepository assignments,
    ISectionAccess sections,
    ICurrentUser currentUser,
    SectionScopeResolver scopes,
    IFileStore files,
    IOptions<StorageOptions> storage,
    ILmsUnitOfWork unitOfWork,
    TimeProvider clock)
{
    // ----- Listing -----

    public async Task<Result<SectionAssignmentsResponse>> ListForSectionAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveForReadingAsync(sectionId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<SectionAssignmentsResponse>(LmsErrors.SectionNotFound);
        }

        var all = await assignments.ListBySectionAsync(sectionId, cancellationToken);
        var visible = scope.CanManage ? all : all.Where(a => a.IsPublished);
        var items = visible.Select(a => a.ToResponse(scope.CanManage ? null : MySubmission(a))).ToList();
        return Result.Success(new SectionAssignmentsResponse(scope.Section, scope.CanManage, items));
    }

    public async Task<Result<AssignmentResponse>> GetAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var (assignment, scope) = await LoadForReadingAsync(assignmentId, cancellationToken);
        return assignment is null
            ? Result.Failure<AssignmentResponse>(LmsErrors.AssignmentNotFound)
            : Result.Success(assignment.ToResponse(scope!.CanManage ? null : MySubmission(assignment)));
    }

    // ----- Management -----

    public async Task<Result<AssignmentResponse>> CreateAsync(Guid sectionId, AssignmentRequest request, CancellationToken cancellationToken)
    {
        if (await scopes.ResolveForManagementAsync(sectionId, cancellationToken) is null)
        {
            return Result.Failure<AssignmentResponse>(LmsErrors.SectionNotFound);
        }

        var assignment = Assignment.Create(sectionId, request.TitleEn, request.TitleAr, request.Instructions, request.MaxScore, request.DueAtUtc, request.AllowLate, request.LatePenaltyPercent);
        assignments.Add(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(assignment.ToResponse());
    }

    public async Task<Result<AssignmentResponse>> UpdateAsync(Guid assignmentId, AssignmentRequest request, CancellationToken cancellationToken)
    {
        var assignment = await LoadForManagementAsync(assignmentId, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure<AssignmentResponse>(LmsErrors.AssignmentNotFound);
        }

        assignment.Amend(request.TitleEn, request.TitleAr, request.Instructions, request.MaxScore, request.DueAtUtc, request.AllowLate, request.LatePenaltyPercent);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(assignment.ToResponse());
    }

    public Task<Result<AssignmentResponse>> PublishAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        TransitionAsync(assignmentId, a => a.Publish(), cancellationToken);

    public Task<Result<AssignmentResponse>> UnpublishAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        TransitionAsync(assignmentId, a => a.Unpublish(), cancellationToken);

    /// <summary>Logical deletion (DC-03); submission files are removed after the commit.</summary>
    public async Task<Result> DeleteAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await LoadForManagementAsync(assignmentId, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure(LmsErrors.AssignmentNotFound);
        }

        var paths = assignment.Submissions.Select(s => s.StoredPath).OfType<string>().ToList();
        assignments.Remove(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var path in paths)
        {
            await files.DeleteAsync(path, cancellationToken);
        }

        return Result.Success();
    }

    // ----- Submissions -----

    /// <summary>Managers: every submission alongside the enrolled learners, so missing work is visible.</summary>
    public async Task<Result<AssignmentSubmissionsResponse>> ListSubmissionsAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await LoadForManagementAsync(assignmentId, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure<AssignmentSubmissionsResponse>(LmsErrors.AssignmentNotFound);
        }

        var learners = await sections.ListEnrolledLearnersAsync(assignment.SectionId, cancellationToken);
        var byUser = learners.ToDictionary(l => l.UserId);
        var submissions = assignment.Submissions
            .Select(s => s.ToResponse(assignment, byUser.GetValueOrDefault(s.LearnerUserId)))
            .OrderBy(s => s.LearnerNumber).ToList();
        return Result.Success(new AssignmentSubmissionsResponse(assignment.ToResponse(), learners, submissions));
    }

    /// <summary>
    /// Learner submission (15.3). BR-09 (active enrolment) and BR-08 (due date) are decided by the aggregate from
    /// facts the SIS contract supplies. An optional file follows the upload controls of 16.5 and lives under
    /// submissions/{assignmentId}/{learnerUserId}; a replaced file is deleted after the commit.
    /// </summary>
    public async Task<Result<SubmissionResponse>> SubmitAsync(Guid assignmentId, SubmitWorkRequest request, FileUpload? upload, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<SubmissionResponse>(LmsErrors.AssignmentNotFound);
        }

        var assignment = await assignments.FindByIdAsync(assignmentId, cancellationToken);
        if (assignment is null || !assignment.IsPublished)
        {
            return Result.Failure<SubmissionResponse>(LmsErrors.AssignmentNotFound);
        }

        var scope = await scopes.ResolveForReadingAsync(assignment.SectionId, cancellationToken);
        if (scope is null || scope.CanManage)
        {
            // Managers do not submit work; a non-member cannot see the assignment (API-06).
            return Result.Failure<SubmissionResponse>(LmsErrors.AssignmentNotFound);
        }

        StoredFile? stored = null;
        if (upload is not null)
        {
            var check = ValidateUpload(upload);
            if (check.IsFailure)
            {
                return Result.Failure<SubmissionResponse>(check.Error);
            }

            stored = await files.SaveAsync($"submissions/{assignment.Id:N}/{userId:N}", Path.GetExtension(upload.FileName), upload.Content, storage.Value.MaxUploadSizeBytes, cancellationToken);
            if (stored is null)
            {
                return Result.Failure<SubmissionResponse>(LmsErrors.UploadTooLarge(storage.Value.MaxUploadSizeBytes));
            }
        }

        var previousPath = assignment.FindSubmissionByLearner(userId)?.StoredPath;
        try
        {
            var enrolled = scope.Role == SectionRole.EnrolledLearner;
            var submission = assignment.Submit(userId, enrolled, request.TextBody, stored?.RelativePath ?? (upload is null ? previousPath : null), clock.GetUtcNow().UtcDateTime);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (stored is not null && previousPath is not null && previousPath != stored.RelativePath)
            {
                await files.DeleteAsync(previousPath, cancellationToken);
            }

            return Result.Success(submission.ToResponse(assignment, null));
        }
        catch
        {
            if (stored is not null)
            {
                await files.DeleteAsync(stored.RelativePath, CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<Result<SubmissionResponse>> MarkAsync(Guid submissionId, MarkSubmissionRequest request, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindBySubmissionIdAsync(submissionId, cancellationToken);
        if (assignment is null || await scopes.ResolveForManagementAsync(assignment.SectionId, cancellationToken) is null)
        {
            return Result.Failure<SubmissionResponse>(LmsErrors.SubmissionNotFound);
        }

        var submission = assignment.Mark(submissionId, request.Score, request.Feedback, currentUser.UserId!.Value, clock.GetUtcNow().UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(submission.ToResponse(assignment, null));
    }

    /// <summary>A submission's file: managers of the section, or the learner who submitted it (SEC-12, SEC-25).</summary>
    public async Task<Result<ResourceDownload>> OpenSubmissionFileAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindBySubmissionIdAsync(submissionId, cancellationToken);
        var submission = assignment?.FindSubmissionOrDefault(submissionId);
        if (assignment is null || submission?.StoredPath is null)
        {
            return Result.Failure<ResourceDownload>(LmsErrors.SubmissionNotFound);
        }

        var scope = await scopes.ResolveForReadingAsync(assignment.SectionId, cancellationToken);
        if (scope is null || (!scope.CanManage && submission.LearnerUserId != currentUser.UserId))
        {
            return Result.Failure<ResourceDownload>(LmsErrors.SubmissionNotFound);
        }

        var stream = await files.OpenReadAsync(submission.StoredPath, cancellationToken);
        if (stream is null)
        {
            return Result.Failure<ResourceDownload>(LmsErrors.SubmissionNotFound);
        }

        var name = $"submission-{submission.LearnerUserId:N}{Path.GetExtension(submission.StoredPath)}";
        return Result.Success(new ResourceDownload(stream, name, "application/octet-stream", stream.CanSeek ? stream.Length : 0, string.Empty));
    }

    // ----- Helpers -----

    private SubmissionResponse? MySubmission(Assignment assignment) =>
        currentUser.UserId is { } userId ? assignment.FindSubmissionByLearner(userId)?.ToResponse(assignment, null) : null;

    private Result ValidateUpload(FileUpload upload)
    {
        var options = storage.Value;
        var extension = Path.GetExtension(upload.FileName);
        if (string.IsNullOrEmpty(extension) || !options.IsPermittedExtension(extension))
        {
            return Result.Failure(LmsErrors.UploadExtensionNotPermitted(options.PermittedExtensions));
        }

        if (upload.Length <= 0)
        {
            return Result.Failure(LmsErrors.UploadEmpty);
        }

        return upload.Length > options.MaxUploadSizeBytes ? Result.Failure(LmsErrors.UploadTooLarge(options.MaxUploadSizeBytes)) : Result.Success();
    }

    private async Task<Result<AssignmentResponse>> TransitionAsync(Guid assignmentId, Action<Assignment> transition, CancellationToken cancellationToken)
    {
        var assignment = await LoadForManagementAsync(assignmentId, cancellationToken);
        if (assignment is null)
        {
            return Result.Failure<AssignmentResponse>(LmsErrors.AssignmentNotFound);
        }

        transition(assignment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(assignment.ToResponse());
    }

    private async Task<Assignment?> LoadForManagementAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(assignmentId, cancellationToken);
        return assignment is null || await scopes.ResolveForManagementAsync(assignment.SectionId, cancellationToken) is null ? null : assignment;
    }

    private async Task<(Assignment? Assignment, SectionScope? Scope)> LoadForReadingAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindByIdAsync(assignmentId, cancellationToken);
        if (assignment is null)
        {
            return (null, null);
        }

        var scope = await scopes.ResolveForReadingAsync(assignment.SectionId, cancellationToken);
        if (scope is null || (!scope.CanManage && !assignment.IsPublished))
        {
            return (null, null);
        }

        return (assignment, scope);
    }
}
