using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Catalogue;

/// <summary>Catalogue browsing with filtering (15.3 "Learner self-service"). Only Open sections are listed. Sort accepts startDate, code.</summary>
public sealed record CatalogueQuery(int? Page, int? PageSize, string? Search, Guid? ProgrammeId, DeliveryMode? DeliveryMode, string? Sort);

public sealed record CatalogueEntry(
    Guid SectionId,
    string SectionCode,
    string TermName,
    DateOnly StartDate,
    DateOnly EndDate,
    DeliveryMode DeliveryMode,
    int Capacity,
    int PlacesRemaining,
    Guid CourseId,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    int Credits,
    Guid ProgrammeId,
    string ProgrammeNameEn,
    string ProgrammeNameAr,
    string? InstructorNameEn,
    string? InstructorNameAr,
    bool IsEnrolled);

public sealed record CatalogueEntryDetail(CatalogueEntry Entry, IReadOnlyList<SessionResponse> Sessions);

public sealed record SelfEnrolRequest(Guid SectionId);

public sealed class CatalogueQueryValidator : AbstractValidator<CatalogueQuery>
{
    public static readonly IReadOnlyList<string> SortFields = ["startDate", "code"];

    public CatalogueQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.DeliveryMode).IsInEnum().When(q => q.DeliveryMode.HasValue);
        RuleFor(q => q.Sort)
            .Must(s => s is null || SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).");
    }
}

public sealed class SelfEnrolRequestValidator : AbstractValidator<SelfEnrolRequest>
{
    public SelfEnrolRequestValidator()
    {
        RuleFor(r => r.SectionId).NotEmpty();
    }
}

/// <summary>
/// Learner self-service (15.3): catalogue browsing, self-enrolment, enrolled course listing, withdrawal
/// and the learner's own transcript. Every operation is scoped to the learner record of the calling
/// user (SEC-12); records of other learners are reported as not found (API-06).
/// </summary>
public sealed class LearnerSelfService(
    ISectionRepository sections,
    ICourseRepository courses,
    IProgrammeRepository programmes,
    ILearnerRepository learners,
    IEnrolmentRepository enrolments,
    IUserDirectory users,
    ICurrentUser currentUser,
    EnrolmentService enrolmentService)
{
    public async Task<PagedResponse<CatalogueEntry>> BrowseAsync(CatalogueQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var (sort, descending) = Sorting.Parse(query.Sort);

        var result = await sections.ListAsync(
            new SectionQuery(page, pageSize, query.Search, null, query.ProgrammeId, SectionStatus.Open, query.DeliveryMode, null, sort ?? "startdate", descending),
            cancellationToken);

        var entries = await ToEntriesAsync(result.Items, cancellationToken);
        return new PagedResponse<CatalogueEntry>(entries, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<CatalogueEntryDetail>> GetAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var section = await sections.FindByIdAsync(sectionId, cancellationToken);
        if (section is null || section.Status != SectionStatus.Open)
        {
            return Result.Failure<CatalogueEntryDetail>(SisErrors.SectionNotFound);
        }

        var entry = (await ToEntriesAsync([section], cancellationToken))[0];
        var sessions = section.Sessions.OrderBy(s => s.ScheduledStartUtc)
            .Select(s => new SessionResponse(s.Id, s.SectionId, s.ScheduledStartUtc, s.ScheduledEndUtc, s.Location)).ToList();
        return Result.Success(new CatalogueEntryDetail(entry, sessions));
    }

    public async Task<Result<EnrolmentResponse>> EnrolAsync(SelfEnrolRequest request, CancellationToken cancellationToken)
    {
        var learner = await CurrentLearnerAsync(cancellationToken);
        if (learner is null)
        {
            return Result.Failure<EnrolmentResponse>(SisErrors.NoLearnerRecordForCaller);
        }

        var section = await sections.FindByIdAsync(request.SectionId, cancellationToken);
        if (section is null || section.Status != SectionStatus.Open)
        {
            // A non-open section is not part of the catalogue the learner can see (API-06).
            return Result.Failure<EnrolmentResponse>(SisErrors.SectionNotFound);
        }

        return Result.Success(await enrolmentService.EnrolAsync(learner, section, cancellationToken));
    }

    public async Task<Result<PagedResponse<EnrolmentResponse>>> ListMyEnrolmentsAsync(int? page, int? pageSize, bool activeOnly, CancellationToken cancellationToken)
    {
        var learner = await CurrentLearnerAsync(cancellationToken);
        if (learner is null)
        {
            return Result.Failure<PagedResponse<EnrolmentResponse>>(SisErrors.NoLearnerRecordForCaller);
        }

        var (p, size) = Paging.Normalise(page, pageSize);
        var result = await enrolments.ListAsync(new EnrolmentQuery(p, size, learner.Id, null, null, activeOnly ? true : null), cancellationToken);
        var items = await enrolmentService.ToResponsesAsync(result.Items, cancellationToken);
        return Result.Success(new PagedResponse<EnrolmentResponse>(items, result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result> WithdrawAsync(Guid enrolmentId, CancellationToken cancellationToken)
    {
        var learner = await CurrentLearnerAsync(cancellationToken);
        if (learner is null)
        {
            return Result.Failure(SisErrors.NoLearnerRecordForCaller);
        }

        var enrolment = await enrolments.FindByIdAsync(enrolmentId, cancellationToken);
        if (enrolment is null || enrolment.LearnerId != learner.Id)
        {
            // SEC-12 / API-06: another learner's enrolment is indistinguishable from a missing one.
            return Result.Failure(SisErrors.EnrolmentNotFound);
        }

        return await enrolmentService.WithdrawAsync(enrolmentId, cancellationToken);
    }

    public async Task<Result<TranscriptResponse>> MyTranscriptAsync(CancellationToken cancellationToken)
    {
        var learner = await CurrentLearnerAsync(cancellationToken);
        return learner is null
            ? Result.Failure<TranscriptResponse>(SisErrors.NoLearnerRecordForCaller)
            : Result.Success(await enrolmentService.BuildTranscriptAsync(learner, cancellationToken));
    }

    private Task<Learner?> CurrentLearnerAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId ? learners.FindByUserIdAsync(userId, cancellationToken) : Task.FromResult<Learner?>(null);

    private async Task<IReadOnlyList<CatalogueEntry>> ToEntriesAsync(IReadOnlyList<CourseSection> items, CancellationToken cancellationToken)
    {
        var coursesById = await courses.FindManyAsync(items.Select(s => s.CourseId), cancellationToken);
        var programmesById = await programmes.FindManyAsync(coursesById.Values.Select(c => c.ProgrammeId), cancellationToken);
        var instructorsById = await users.FindManyAsync(items.Select(s => s.InstructorUserId), cancellationToken);
        var activeCounts = await enrolments.CountActiveInSectionsAsync(items.Select(s => s.Id), cancellationToken);

        var learner = await CurrentLearnerAsync(cancellationToken);
        var enrolledIn = new HashSet<Guid>();
        if (learner is not null)
        {
            var mine = await enrolments.ListAsync(new EnrolmentQuery(1, Paging.MaxPageSize, learner.Id, null, null, true), cancellationToken);
            enrolledIn = mine.Items.Select(e => e.SectionId).ToHashSet();
        }

        return items.Select(s =>
        {
            var course = coursesById[s.CourseId];
            var programme = programmesById[course.ProgrammeId];
            instructorsById.TryGetValue(s.InstructorUserId, out var instructor);
            var active = activeCounts.TryGetValue(s.Id, out var n) ? n : 0;

            return new CatalogueEntry(
                s.Id, s.Code, s.TermName, s.StartDate, s.EndDate, s.DeliveryMode, s.Capacity, Math.Max(s.Capacity - active, 0),
                course.Id, course.Code, course.NameEn, course.NameAr, course.DescriptionEn, course.DescriptionAr, course.Credits,
                programme.Id, programme.NameEn, programme.NameAr,
                instructor?.FullNameEn, instructor?.FullNameAr,
                enrolledIn.Contains(s.Id));
        }).ToList();
    }
}
