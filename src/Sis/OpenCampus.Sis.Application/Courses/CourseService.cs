using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.Sis.Application.Courses;

/// <summary>Course listing, creation, retrieval, amendment and deletion (15.3 "Academic structure").</summary>
public sealed class CourseService(ICourseRepository courses, IProgrammeRepository programmes, ISisUnitOfWork unitOfWork)
{
    public async Task<PagedResponse<CourseResponse>> ListAsync(CourseListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var (sort, descending) = Sorting.Parse(query.Sort);

        var result = await courses.ListAsync(new CourseQuery(page, pageSize, query.Search, query.ProgrammeId, sort, descending), cancellationToken);
        var programmesById = await programmes.FindManyAsync(result.Items.Select(c => c.ProgrammeId), cancellationToken);

        return new PagedResponse<CourseResponse>(
            result.Items.Select(c => ToResponse(c, programmesById[c.ProgrammeId])).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<CourseResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var course = await courses.FindByIdAsync(id, cancellationToken);
        if (course is null)
        {
            return Result.Failure<CourseResponse>(SisErrors.CourseNotFound);
        }

        var programme = await programmes.FindByIdAsync(course.ProgrammeId, cancellationToken);
        return Result.Success(ToResponse(course, programme!));
    }

    public async Task<Result<CourseResponse>> CreateAsync(CreateCourseRequest request, CancellationToken cancellationToken)
    {
        var programme = await programmes.FindByIdAsync(request.ProgrammeId, cancellationToken);
        if (programme is null)
        {
            return Result.Failure<CourseResponse>(Error.Validation("ProgrammeId", "The programme does not exist."));
        }

        if (await courses.CodeExistsAsync(request.Code.Trim().ToUpperInvariant(), cancellationToken))
        {
            return Result.Failure<CourseResponse>(SisErrors.CourseCodeTaken);
        }

        var course = Course.Create(programme.Id, request.Code, request.NameEn, request.NameAr, request.DescriptionEn, request.DescriptionAr, request.Credits);
        courses.Add(course);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(course, programme));
    }

    public async Task<Result<CourseResponse>> UpdateAsync(Guid id, UpdateCourseRequest request, CancellationToken cancellationToken)
    {
        var course = await courses.FindByIdAsync(id, cancellationToken);
        if (course is null)
        {
            return Result.Failure<CourseResponse>(SisErrors.CourseNotFound);
        }

        course.Amend(request.NameEn, request.NameAr, request.DescriptionEn, request.DescriptionAr, request.Credits);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var programme = await programmes.FindByIdAsync(course.ProgrammeId, cancellationToken);
        return Result.Success(ToResponse(course, programme!));
    }

    /// <summary>Logical deletion (DC-03); refused while sections belong to the course.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var course = await courses.FindByIdAsync(id, cancellationToken);
        if (course is null)
        {
            return Result.Failure(SisErrors.CourseNotFound);
        }

        if (await courses.CountSectionsAsync(id, cancellationToken) > 0)
        {
            return Result.Failure(SisErrors.CourseHasSections);
        }

        course.MarkDeleted();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static CourseResponse ToResponse(Course c, Programme p) => new(
        c.Id, p.Id, p.Code, p.NameEn, p.NameAr,
        c.Code, c.NameEn, c.NameAr, c.DescriptionEn, c.DescriptionAr, c.Credits,
        c.CreatedAtUtc, c.ModifiedAtUtc);
}
