using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.Sis.Application.Programmes;

/// <summary>Programme listing, creation, retrieval, amendment and deletion (15.3 "Academic structure").</summary>
public sealed class ProgrammeService(IProgrammeRepository programmes, ISisUnitOfWork unitOfWork)
{
    public async Task<PagedResponse<ProgrammeResponse>> ListAsync(ProgrammeListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var (sort, descending) = Sorting.Parse(query.Sort);

        var result = await programmes.ListAsync(new ProgrammeQuery(page, pageSize, query.Search, query.IsActive, sort, descending), cancellationToken);
        return new PagedResponse<ProgrammeResponse>(result.Items.Select(ToResponse).ToList(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<ProgrammeResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var programme = await programmes.FindByIdAsync(id, cancellationToken);
        return programme is null ? Result.Failure<ProgrammeResponse>(SisErrors.ProgrammeNotFound) : Result.Success(ToResponse(programme));
    }

    public async Task<Result<ProgrammeResponse>> CreateAsync(CreateProgrammeRequest request, CancellationToken cancellationToken)
    {
        if (await programmes.CodeExistsAsync(request.Code.Trim().ToUpperInvariant(), cancellationToken))
        {
            return Result.Failure<ProgrammeResponse>(SisErrors.ProgrammeCodeTaken);
        }

        var programme = Programme.Create(request.Code, request.NameEn, request.NameAr, request.DurationMonths);
        programmes.Add(programme);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(programme));
    }

    public async Task<Result<ProgrammeResponse>> UpdateAsync(Guid id, UpdateProgrammeRequest request, CancellationToken cancellationToken)
    {
        var programme = await programmes.FindByIdAsync(id, cancellationToken);
        if (programme is null)
        {
            return Result.Failure<ProgrammeResponse>(SisErrors.ProgrammeNotFound);
        }

        programme.Amend(request.NameEn, request.NameAr, request.DurationMonths);
        if (request.IsActive)
        {
            programme.Activate();
        }
        else
        {
            programme.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(programme));
    }

    /// <summary>Logical deletion (DC-03); refused while courses belong to the programme so that no orphan is created.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var programme = await programmes.FindByIdAsync(id, cancellationToken);
        if (programme is null)
        {
            return Result.Failure(SisErrors.ProgrammeNotFound);
        }

        if (await programmes.CountCoursesAsync(id, cancellationToken) > 0)
        {
            return Result.Failure(SisErrors.ProgrammeHasCourses);
        }

        programme.MarkDeleted();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static ProgrammeResponse ToResponse(Programme p) =>
        new(p.Id, p.Code, p.NameEn, p.NameAr, p.DurationMonths, p.IsActive, p.CreatedAtUtc, p.ModifiedAtUtc);
}

/// <summary>Parses the API-03 sort parameter: field name, optionally prefixed with '-' for descending.</summary>
internal static class Sorting
{
    public static (string? Field, bool Descending) Parse(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return (null, false);
        }

        var descending = sort.StartsWith('-');
        return (sort.TrimStart('-').ToLowerInvariant(), descending);
    }
}
