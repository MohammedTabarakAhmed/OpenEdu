using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Enrolments;

/// <summary>
/// Enrolment creation and withdrawal, listing by learner and by section, and transcript retrieval
/// (15.3 "Learner and enrolment"). BR-01, BR-02 and BR-03 are enforced by the aggregates at creation.
/// </summary>
public sealed class EnrolmentService(
    IEnrolmentRepository enrolments,
    ILearnerRepository learners,
    ISectionRepository sections,
    ICourseRepository courses,
    IUserDirectory users,
    ISisUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<PagedResponse<EnrolmentResponse>> ListAsync(EnrolmentListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var result = await enrolments.ListAsync(new EnrolmentQuery(page, pageSize, query.LearnerId, query.SectionId, query.Status, null), cancellationToken);
        var items = await ToResponsesAsync(result.Items, cancellationToken);
        return new PagedResponse<EnrolmentResponse>(items, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<EnrolmentResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.FindByIdAsync(id, cancellationToken);
        return enrolment is null
            ? Result.Failure<EnrolmentResponse>(SisErrors.EnrolmentNotFound)
            : Result.Success((await ToResponsesAsync([enrolment], cancellationToken))[0]);
    }

    public async Task<Result<EnrolmentResponse>> CreateAsync(CreateEnrolmentRequest request, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByIdAsync(request.LearnerId, cancellationToken);
        if (learner is null)
        {
            return Result.Failure<EnrolmentResponse>(Error.Validation("LearnerId", "The learner does not exist."));
        }

        var section = await sections.FindByIdAsync(request.SectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<EnrolmentResponse>(Error.Validation("SectionId", "The section does not exist."));
        }

        return Result.Success(await EnrolAsync(learner, section, cancellationToken));
    }

    public async Task<Result> WithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.FindByIdAsync(id, cancellationToken);
        if (enrolment is null)
        {
            return Result.Failure(SisErrors.EnrolmentNotFound);
        }

        enrolment.Withdraw();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<TranscriptResponse>> GetTranscriptAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByIdAsync(learnerId, cancellationToken);
        if (learner is null)
        {
            return Result.Failure<TranscriptResponse>(SisErrors.LearnerNotFound);
        }

        return Result.Success(await BuildTranscriptAsync(learner, cancellationToken));
    }

    // ----- Shared with learner self-service -----

    /// <summary>Applies the section-14 preconditions through the aggregates and persists the enrolment.</summary>
    internal async Task<EnrolmentResponse> EnrolAsync(Learner learner, CourseSection section, CancellationToken cancellationToken)
    {
        var activeCount = await enrolments.CountActiveInSectionAsync(section.Id, cancellationToken);
        var existing = await enrolments.FindByLearnerAndSectionAsync(learner.Id, section.Id, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        Enrolment enrolment;
        if (existing is null)
        {
            enrolment = Enrolment.Create(learner, section, activeCount, learnerHasActiveEnrolmentInSection: false, now);
            enrolments.Add(enrolment);
        }
        else
        {
            // One row per (learner, section): an active row is a BR-02 violation, a withdrawn one is reinstated.
            existing.Reinstate(learner, section, activeCount, now);
            enrolment = existing;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (await ToResponsesAsync([enrolment], cancellationToken))[0];
    }

    internal async Task<TranscriptResponse> BuildTranscriptAsync(Learner learner, CancellationToken cancellationToken)
    {
        // A transcript is bounded by the learner's own history; the page size ceiling still applies (NFR-03).
        var page = await enrolments.ListAsync(new EnrolmentQuery(1, Paging.MaxPageSize, learner.Id, null, null, null), cancellationToken);
        var items = await ToResponsesAsync(page.Items, cancellationToken);
        var user = await users.FindAsync(learner.UserId, cancellationToken);
        var completed = items.Where(e => e.Status == EnrolmentStatus.Completed).ToList();

        return new TranscriptResponse(
            learner.Id, learner.LearnerNumber, user?.FullNameEn, user?.FullNameAr,
            items, completed.Count, completed.Sum(e => e.Section.Credits));
    }

    internal async Task<IReadOnlyList<EnrolmentResponse>> ToResponsesAsync(IReadOnlyList<Enrolment> items, CancellationToken cancellationToken)
    {
        // Batched lookups: learners, sections, courses, then one Identity resolution for learners and instructors (NFR-04).
        var learnersById = await learners.FindManyAsync(items.Select(e => e.LearnerId), cancellationToken);
        var sectionsById = await sections.FindManyAsync(items.Select(e => e.SectionId), cancellationToken);
        var coursesById = await courses.FindManyAsync(sectionsById.Values.Select(s => s.CourseId), cancellationToken);
        var userIds = learnersById.Values.Select(l => l.UserId).Concat(sectionsById.Values.Select(s => s.InstructorUserId));
        var usersById = await users.FindManyAsync(userIds, cancellationToken);

        return items.Select(e =>
        {
            var learner = learnersById[e.LearnerId];
            var section = sectionsById[e.SectionId];
            var course = coursesById[section.CourseId];
            usersById.TryGetValue(learner.UserId, out var learnerUser);
            usersById.TryGetValue(section.InstructorUserId, out var instructor);

            return new EnrolmentResponse(
                e.Id,
                new EnrolmentLearnerSummary(learner.Id, learner.LearnerNumber, learner.UserId, learnerUser?.UserName, learnerUser?.FullNameEn, learnerUser?.FullNameAr),
                ToSectionSummary(section, course, instructor),
                e.EnrolledAtUtc, e.Status, e.FinalGrade, e.CompletedAtUtc);
        }).ToList();
    }

    internal static EnrolmentSectionSummary ToSectionSummary(CourseSection section, Course course, UserSummary? instructor) => new(
        section.Id, section.Code, section.TermName, section.StartDate, section.EndDate,
        course.Id, course.Code, course.NameEn, course.NameAr, course.Credits,
        instructor?.FullNameEn, instructor?.FullNameAr);
}
