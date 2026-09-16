using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Sections;

/// <summary>
/// Section listing, creation, retrieval, amendment, deletion, state transition, scheduled session
/// management and grade scheme definition (15.3 "Academic structure"). Rule violations raised by the
/// aggregate propagate as <see cref="BusinessRuleViolationException"/> and become 422 (18.3).
/// </summary>
public sealed class SectionService(
    ISectionRepository sections,
    ICourseRepository courses,
    IEnrolmentRepository enrolments,
    IUserDirectory users,
    ISisUnitOfWork unitOfWork)
{
    /// <summary>The Identity role an instructor reference must hold (2.3).</summary>
    public const string InstructorRole = "Instructor";

    public async Task<PagedResponse<SectionResponse>> ListAsync(SectionListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var (sort, descending) = Sorting.Parse(query.Sort);

        var result = await sections.ListAsync(
            new SectionQuery(page, pageSize, query.Search, query.CourseId, query.ProgrammeId, query.Status, query.DeliveryMode, query.InstructorUserId, sort, descending),
            cancellationToken);

        var items = await ToResponsesAsync(result.Items, cancellationToken);
        return new PagedResponse<SectionResponse>(items, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<SectionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(id, cancellationToken);
        return section is null
            ? Result.Failure<SectionDetailResponse>(SisErrors.SectionNotFound)
            : Result.Success(await ToDetailAsync(section, cancellationToken));
    }

    public async Task<Result<SectionDetailResponse>> CreateAsync(CreateSectionRequest request, CancellationToken cancellationToken)
    {
        var course = await courses.FindByIdAsync(request.CourseId, cancellationToken);
        if (course is null)
        {
            return Result.Failure<SectionDetailResponse>(Error.Validation("CourseId", "The course does not exist."));
        }

        if (await sections.CodeExistsAsync(course.Id, request.Code.Trim().ToUpperInvariant(), cancellationToken))
        {
            return Result.Failure<SectionDetailResponse>(SisErrors.SectionCodeTaken);
        }

        var instructor = await ValidateInstructorAsync(request.InstructorUserId, cancellationToken);
        if (instructor.IsFailure)
        {
            return Result.Failure<SectionDetailResponse>(instructor.Error);
        }

        var section = CourseSection.Create(
            course.Id, request.Code, request.TermName, request.StartDate, request.EndDate,
            request.Capacity, request.InstructorUserId, request.DeliveryMode);
        sections.Add(section);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailAsync(section, cancellationToken));
    }

    public async Task<Result<SectionDetailResponse>> UpdateAsync(Guid id, UpdateSectionRequest request, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(id, cancellationToken);
        if (section is null)
        {
            return Result.Failure<SectionDetailResponse>(SisErrors.SectionNotFound);
        }

        if (request.InstructorUserId != section.InstructorUserId)
        {
            var instructor = await ValidateInstructorAsync(request.InstructorUserId, cancellationToken);
            if (instructor.IsFailure)
            {
                return Result.Failure<SectionDetailResponse>(instructor.Error);
            }
        }

        var active = await enrolments.CountActiveInSectionAsync(id, cancellationToken);
        section.Amend(request.TermName, request.StartDate, request.EndDate, request.Capacity, request.InstructorUserId, request.DeliveryMode, active);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToDetailAsync(section, cancellationToken));
    }

    /// <summary>Logical deletion (DC-03) guarded by BR-14.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(id, cancellationToken);
        if (section is null)
        {
            return Result.Failure(SisErrors.SectionNotFound);
        }

        section.EnsureDeletable(await enrolments.CountInSectionAsync(id, cancellationToken));
        section.MarkDeleted();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ----- State transitions -----

    public Task<Result<SectionDetailResponse>> OpenAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, s => s.Open(), cancellationToken);

    public Task<Result<SectionDetailResponse>> CloseAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, s => s.Close(), cancellationToken);

    public Task<Result<SectionDetailResponse>> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, s => s.Cancel(), cancellationToken);

    private async Task<Result<SectionDetailResponse>> TransitionAsync(Guid id, Action<CourseSection> transition, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(id, cancellationToken);
        if (section is null)
        {
            return Result.Failure<SectionDetailResponse>(SisErrors.SectionNotFound);
        }

        transition(section);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(await ToDetailAsync(section, cancellationToken));
    }

    // ----- Scheduled sessions -----

    public async Task<Result<SessionResponse>> AddSessionAsync(Guid sectionId, SessionRequest request, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<SessionResponse>(SisErrors.SectionNotFound);
        }

        var elsewhere = await sections.GetInstructorSessionsElsewhereAsync(section.InstructorUserId, sectionId, cancellationToken);
        var session = section.AddSession(request.ScheduledStartUtc, request.ScheduledEndUtc, request.Location, elsewhere);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(session));
    }

    public async Task<Result<SessionResponse>> RescheduleSessionAsync(Guid sectionId, Guid sessionId, SessionRequest request, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<SessionResponse>(SisErrors.SectionNotFound);
        }

        if (section.Sessions.All(s => s.Id != sessionId))
        {
            return Result.Failure<SessionResponse>(SisErrors.SessionNotFound);
        }

        var elsewhere = await sections.GetInstructorSessionsElsewhereAsync(section.InstructorUserId, sectionId, cancellationToken);
        var session = section.RescheduleSession(sessionId, request.ScheduledStartUtc, request.ScheduledEndUtc, request.Location, elsewhere);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(session));
    }

    public async Task<Result> RemoveSessionAsync(Guid sectionId, Guid sessionId, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure(SisErrors.SectionNotFound);
        }

        if (section.Sessions.All(s => s.Id != sessionId))
        {
            return Result.Failure(SisErrors.SessionNotFound);
        }

        section.RemoveSession(sessionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ----- Grade scheme -----

    public async Task<Result<GradeComponentResponse>> AddGradeComponentAsync(Guid sectionId, GradeComponentRequest request, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<GradeComponentResponse>(SisErrors.SectionNotFound);
        }

        var component = section.AddGradeComponent(request.NameEn, request.NameAr, request.WeightPercent, request.MaxScore);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(component));
    }

    public async Task<Result<GradeComponentResponse>> AmendGradeComponentAsync(Guid sectionId, Guid componentId, GradeComponentRequest request, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure<GradeComponentResponse>(SisErrors.SectionNotFound);
        }

        if (section.GradeComponents.All(c => c.Id != componentId))
        {
            return Result.Failure<GradeComponentResponse>(SisErrors.GradeComponentNotFound);
        }

        var component = section.AmendGradeComponent(componentId, request.NameEn, request.NameAr, request.WeightPercent, request.MaxScore);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(component));
    }

    public async Task<Result> RemoveGradeComponentAsync(Guid sectionId, Guid componentId, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return Result.Failure(SisErrors.SectionNotFound);
        }

        if (section.GradeComponents.All(c => c.Id != componentId))
        {
            return Result.Failure(SisErrors.GradeComponentNotFound);
        }

        section.RemoveGradeComponent(componentId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Instructors available for assignment (active users in the Instructor role), for the administrative interface.</summary>
    public async Task<IReadOnlyList<InstructorSummary>> ListInstructorsAsync(CancellationToken cancellationToken) =>
        (await users.ListActiveInRoleAsync(InstructorRole, cancellationToken)).Select(ToInstructor).ToList();

    // ----- Helpers -----

    /// <summary>13.5: the cross-module reference is validated through the Identity contract at the point of creation.</summary>
    private async Task<Result> ValidateInstructorAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindAsync(instructorUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure(SisErrors.UnknownUser("InstructorUserId"));
        }

        return user.Roles.Contains(InstructorRole, StringComparer.OrdinalIgnoreCase)
            ? Result.Success()
            : Result.Failure(SisErrors.UserLacksRole("InstructorUserId", InstructorRole));
    }

    internal async Task<IReadOnlyList<SectionResponse>> ToResponsesAsync(IReadOnlyList<CourseSection> items, CancellationToken cancellationToken)
    {
        // Three batched lookups regardless of page size (NFR-04).
        var coursesById = await courses.FindManyAsync(items.Select(s => s.CourseId), cancellationToken);
        var instructorsById = await users.FindManyAsync(items.Select(s => s.InstructorUserId), cancellationToken);
        var activeCounts = await enrolments.CountActiveInSectionsAsync(items.Select(s => s.Id), cancellationToken);

        return items.Select(s => ToResponse(
            s,
            coursesById[s.CourseId],
            instructorsById.TryGetValue(s.InstructorUserId, out var u) ? u : null,
            activeCounts.TryGetValue(s.Id, out var n) ? n : 0)).ToList();
    }

    private async Task<SectionDetailResponse> ToDetailAsync(CourseSection section, CancellationToken cancellationToken)
    {
        var summary = (await ToResponsesAsync([section], cancellationToken))[0];
        return new SectionDetailResponse(
            summary,
            section.Sessions.OrderBy(s => s.ScheduledStartUtc).Select(ToResponse).ToList(),
            section.GradeComponents.OrderBy(c => c.CreatedAtUtc).Select(ToResponse).ToList(),
            section.TotalWeightPercent);
    }

    internal static SectionResponse ToResponse(CourseSection s, Course course, UserSummary? instructor, int activeEnrolments) => new(
        s.Id, course.Id, course.Code, course.NameEn, course.NameAr, course.ProgrammeId,
        s.Code, s.TermName, s.StartDate, s.EndDate, s.Capacity, activeEnrolments,
        instructor is null ? null : ToInstructor(instructor),
        s.DeliveryMode, s.Status, s.CreatedAtUtc, s.ModifiedAtUtc);

    private static InstructorSummary ToInstructor(UserSummary u) => new(u.Id, u.UserName, u.FullNameEn, u.FullNameAr, u.IsActive);

    private static SessionResponse ToResponse(Session s) => new(s.Id, s.SectionId, s.ScheduledStartUtc, s.ScheduledEndUtc, s.Location);

    private static GradeComponentResponse ToResponse(GradeComponent c) => new(c.Id, c.SectionId, c.NameEn, c.NameAr, c.WeightPercent, c.MaxScore);
}
