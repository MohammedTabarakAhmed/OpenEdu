using System.Text.Json;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Grading;

/// <summary>
/// Section gradebook retrieval, grade recording and grade release (15.3 "Assessment and grading"). Scope (SEC-12):
/// the section's assigned instructor or a holder of section administration; everyone else is answered "not found"
/// (API-06). BR-05 and BR-07 are enforced by the aggregates; every grade creation, amendment and release is
/// written to the audit trail (SEC-30) through the Identity contract.
/// </summary>
public sealed class GradingService(
    ISectionRepository sections,
    ICourseRepository courses,
    IEnrolmentRepository enrolments,
    IGradeEntryRepository grades,
    ILearnerRepository learners,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditTrail audit,
    LearnerNotifier notifier,
    ISisUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>The Appendix C capability that confers management of any section's grades, alongside the assigned instructor.</summary>
    public const string SectionAdministration = "sis.section.write";

    public async Task<Result<GradebookResponse>> GetGradebookAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var section = await LoadManagedSectionAsync(sectionId, cancellationToken);
        return section is null
            ? Result.Failure<GradebookResponse>(SisErrors.SectionNotFound)
            : Result.Success(await BuildGradebookAsync(section, cancellationToken));
    }

    /// <summary>Creates or amends the entry; BR-05 in the aggregate; SEC-30 audit on both paths.</summary>
    public async Task<Result<GradeEntryResponse>> RecordGradeAsync(Guid sectionId, RecordGradeRequest request, CancellationToken cancellationToken)
    {
        var section = await LoadManagedSectionAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<GradeEntryResponse>(SisErrors.SectionNotFound);
        }

        var enrolment = await enrolments.FindByIdAsync(request.EnrolmentId, cancellationToken);
        if (enrolment is null || enrolment.SectionId != section.Id)
        {
            return Result.Failure<GradeEntryResponse>(SisErrors.EnrolmentNotFound);
        }

        var component = section.GradeComponents.SingleOrDefault(c => c.Id == request.GradeComponentId);
        if (component is null)
        {
            return Result.Failure<GradeEntryResponse>(SisErrors.GradeComponentNotFound);
        }

        var actor = currentUser.UserId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await grades.FindByEnrolmentAndComponentAsync(enrolment.Id, component.Id, cancellationToken);
        GradeEntry entry;
        string eventType;
        string details;

        if (existing is null)
        {
            entry = GradeEntry.Create(enrolment, component, request.Score, actor, now);
            grades.Add(entry);
            eventType = SisAuditEventTypes.GradeCreated;
            details = JsonSerializer.Serialize(new { sectionId, enrolmentId = enrolment.Id, componentId = component.Id, score = request.Score });
        }
        else
        {
            var previous = existing.Score;
            existing.Amend(component, request.Score, actor, now);
            entry = existing;
            eventType = SisAuditEventTypes.GradeAmended;
            details = JsonSerializer.Serialize(new { sectionId, enrolmentId = enrolment.Id, componentId = component.Id, previousScore = previous, score = request.Score });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(eventType, nameof(GradeEntry), entry.Id, details, cancellationToken);
        return Result.Success(ToResponse(entry));
    }

    /// <summary>
    /// Releases every entry of the section (BR-07 via the aggregate), records each active enrolment's weighted final
    /// grade and completes it; one audit record for the release (SEC-30).
    /// </summary>
    public async Task<Result<GradebookResponse>> ReleaseAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var section = await LoadManagedSectionAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<GradebookResponse>(SisErrors.SectionNotFound);
        }

        var active = await enrolments.ListActiveInSectionAsync(section.Id, cancellationToken);
        var entries = await grades.ListBySectionAsync(section.Id, cancellationToken);
        var byEnrolment = entries.ToLookup(e => e.EnrolmentId);

        section.EnsureGradesReleasable(CountUngradedPairs(section, active, byEnrolment));

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var enrolment in active)
        {
            var scores = byEnrolment[enrolment.Id].ToDictionary(e => e.GradeComponentId, e => e.Score);
            enrolment.Complete(section.ComputeFinalGrade(scores), now);
        }

        foreach (var entry in entries)
        {
            entry.Release();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            SisAuditEventTypes.GradesReleased, nameof(CourseSection), section.Id,
            JsonSerializer.Serialize(new { sectionId = section.Id, enrolmentsCompleted = active.Count, entriesReleased = entries.Count }),
            cancellationToken);

        // EXT-01: the completed learners are told; a dispatch failure is logged by the notifier and never undoes the release.
        var course = (await courses.FindManyAsync([section.CourseId], cancellationToken))[section.CourseId];
        var completedLearners = (await learners.FindManyAsync(active.Select(e => e.LearnerId), cancellationToken)).Values.ToList();
        await notifier.GradesReleasedAsync(completedLearners, section.Code, course.Code, course.NameEn, cancellationToken);

        return Result.Success(await BuildGradebookAsync(section, cancellationToken));
    }

    // ----- Learner side (BR-06) -----

    /// <summary>The results of one enrolment as its learner may see them: released entries only; the final grade once completed.</summary>
    public async Task<LearnerResultsResponse> BuildLearnerResultsAsync(Enrolment enrolment, CancellationToken cancellationToken)
    {
        var section = (await sections.FindByIdAsync(enrolment.SectionId, cancellationToken))!;
        var course = (await courses.FindManyAsync([section.CourseId], cancellationToken))[section.CourseId];
        var entries = await grades.ListByEnrolmentAsync(enrolment.Id, cancellationToken);
        var visible = enrolment.GradesVisibleToLearner(entries).Select(ToResponse).ToList();

        return new LearnerResultsResponse(
            enrolment.Id, section.Id, section.Code, course.Code, course.NameEn, course.NameAr,
            enrolment.Status, enrolment.Status == EnrolmentStatus.Completed ? enrolment.FinalGrade : null,
            visible.Count > 0 && visible.Count == entries.Count,
            section.GradeComponents.Select(ToComponent).ToList(), visible);
    }

    // ----- Helpers -----

    private async Task<CourseSection?> LoadManagedSectionAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return null;
        }

        return section.InstructorUserId == userId || currentUser.HasPermission(SectionAdministration) ? section : null;
    }

    private static int CountUngradedPairs(CourseSection section, IReadOnlyList<Enrolment> active, ILookup<Guid, GradeEntry> byEnrolment) =>
        active.Sum(e => section.GradeComponents.Count(c => !byEnrolment[e.Id].Any(g => g.GradeComponentId == c.Id)));

    private async Task<GradebookResponse> BuildGradebookAsync(CourseSection section, CancellationToken cancellationToken)
    {
        var course = (await courses.FindManyAsync([section.CourseId], cancellationToken))[section.CourseId];
        var rowsSource = await enrolments.ListGradableInSectionAsync(section.Id, cancellationToken);
        var active = rowsSource.Where(e => e.IsActive).ToList();
        var entries = await grades.ListBySectionAsync(section.Id, cancellationToken);
        var byEnrolment = entries.ToLookup(e => e.EnrolmentId);
        var learnersById = await learners.FindManyAsync(rowsSource.Select(e => e.LearnerId), cancellationToken);
        var usersById = await users.FindManyAsync(learnersById.Values.Select(l => l.UserId), cancellationToken);

        var rows = rowsSource.Select(e =>
        {
            var learner = learnersById[e.LearnerId];
            usersById.TryGetValue(learner.UserId, out var user);
            return new GradebookRow(
                e.Id, learner.Id, learner.LearnerNumber, learner.UserId,
                user?.FullNameEn ?? learner.LearnerNumber, user?.FullNameAr ?? learner.LearnerNumber,
                e.Status, e.FinalGrade, byEnrolment[e.Id].Select(ToResponse).ToList());
        }).OrderBy(r => r.LearnerNumber).ToList();

        return new GradebookResponse(
            section.Id, section.Code, course.Code, course.NameEn, course.NameAr,
            section.GradeComponents.Select(ToComponent).ToList(), rows,
            CountUngradedPairs(section, active, byEnrolment),
            entries.Count > 0 && entries.All(e => e.IsReleased));
    }

    private static GradebookComponent ToComponent(GradeComponent c) => new(c.Id, c.NameEn, c.NameAr, c.WeightPercent, c.MaxScore);

    private static GradeEntryResponse ToResponse(GradeEntry g) => new(g.Id, g.EnrolmentId, g.GradeComponentId, g.Score, g.IsReleased, g.GradedByUserId, g.GradedAtUtc);
}
