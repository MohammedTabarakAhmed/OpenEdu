using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Api.Integration;

/// <summary>Implements the LMS→SIS "assessment outcomes" contract (6.5) at the composition root: attendance rates feed BR-12.</summary>
public sealed class SisAssessmentOutcomes(EnrolmentStandingService standing) : IAssessmentOutcomes
{
    public Task ReportAttendanceRateAsync(Guid sectionId, Guid learnerUserId, decimal attendancePercent, CancellationToken cancellationToken) =>
        standing.ApplyAttendanceRateAsync(sectionId, learnerUserId, attendancePercent, cancellationToken);
}

/// <summary>
/// Implements the LMS module's contract to SIS (6.5: "confirm a learner's enrolment in a section; retrieve
/// section and instructor assignment"; MB-02). Lives in the host because composition of module
/// implementations occurs only here (MB-05) and no module may reference another's application layer (MB-04).
/// Reads SIS exclusively through its published repository contracts, never its schema (MB-01). Every answer
/// feeds SEC-12: it is the only way the LMS learns who is assigned to, or enrolled in, a section.
/// </summary>
public sealed class SisSectionAccess(
    ISectionRepository sections,
    ICourseRepository courses,
    ILearnerRepository learners,
    IEnrolmentRepository enrolments,
    IUserDirectory users) : ISectionAccess
{
    public async Task<SectionSummary?> FindSectionAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var found = await sections.FindManyAsync([sectionId], cancellationToken);
        return found.TryGetValue(sectionId, out var section)
            ? (await ToSummariesAsync([section], cancellationToken)).SingleOrDefault()
            : null;
    }

    public async Task<bool> IsLearnerEnrolledAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByUserIdAsync(userId, cancellationToken);
        if (learner is null)
        {
            return false;
        }

        var enrolment = await enrolments.FindByLearnerAndSectionAsync(learner.Id, sectionId, cancellationToken);
        return enrolment is { IsActive: true };
    }

    public async Task<IReadOnlyList<SectionSummary>> ListSectionsForInstructorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var page = await sections.ListAsync(
            new SectionQuery(1, Paging.MaxPageSize, null, null, null, null, null, userId, "startdate", Descending: true), cancellationToken);
        return await ToSummariesAsync(page.Items.Where(s => s.Status != SectionStatus.Cancelled).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<SectionSummary>> ListSectionsForLearnerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByUserIdAsync(userId, cancellationToken);
        if (learner is null)
        {
            return [];
        }

        var active = await enrolments.ListAsync(new EnrolmentQuery(1, Paging.MaxPageSize, learner.Id, null, null, ActiveOnly: true), cancellationToken);
        var byId = await sections.FindManyAsync(active.Items.Select(e => e.SectionId), cancellationToken);
        var ordered = byId.Values.OrderByDescending(s => s.StartDate).ThenBy(s => s.Code).ToList();
        return await ToSummariesAsync(ordered, cancellationToken);
    }

    public async Task<IReadOnlyList<SectionSummary>> ListLiveSectionsAsync(CancellationToken cancellationToken)
    {
        var live = new List<CourseSection>();
        foreach (var status in new[] { SectionStatus.Draft, SectionStatus.Open })
        {
            var page = await sections.ListAsync(new SectionQuery(1, Paging.MaxPageSize, null, null, null, status, null, null, null, false), cancellationToken);
            live.AddRange(page.Items);
        }

        return await ToSummariesAsync(live, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        return section is null
            ? []
            : section.Sessions.OrderBy(s => s.ScheduledStartUtc)
                .Select(s => new SessionSummary(s.Id, s.SectionId, s.ScheduledStartUtc, s.ScheduledEndUtc, s.Location)).ToList();
    }

    /// <summary>Active enrolments → learner records → Identity display names: three batched lookups (NFR-04).</summary>
    public async Task<IReadOnlyList<EnrolledLearner>> ListEnrolledLearnersAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var active = await enrolments.ListActiveInSectionAsync(sectionId, cancellationToken);
        if (active.Count == 0)
        {
            return [];
        }

        var learnersById = await learners.FindManyAsync(active.Select(e => e.LearnerId), cancellationToken);
        var usersById = await users.FindManyAsync(learnersById.Values.Select(l => l.UserId), cancellationToken);
        return active
            .Select(e => learnersById[e.LearnerId])
            .Select(l =>
            {
                usersById.TryGetValue(l.UserId, out var user);
                return new EnrolledLearner(l.UserId, l.LearnerNumber, user?.FullNameEn ?? l.LearnerNumber, user?.FullNameAr ?? l.LearnerNumber);
            })
            .OrderBy(l => l.LearnerNumber)
            .ToList();
    }

    /// <summary>One batched course lookup per call (NFR-04).</summary>
    private async Task<IReadOnlyList<SectionSummary>> ToSummariesAsync(IReadOnlyList<CourseSection> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var coursesById = await courses.FindManyAsync(items.Select(s => s.CourseId), cancellationToken);
        return items.Select(s =>
        {
            var course = coursesById[s.CourseId];
            return new SectionSummary(s.Id, s.Code, s.TermName, s.Status.ToString(), course.Id, course.Code, course.NameEn, course.NameAr, s.InstructorUserId);
        }).ToList();
    }
}
