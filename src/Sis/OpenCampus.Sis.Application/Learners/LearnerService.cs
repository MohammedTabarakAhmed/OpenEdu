using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Domain.Learners;

namespace OpenCampus.Sis.Application.Learners;

/// <summary>Learner listing, creation, retrieval and amendment (15.3 "Learner and enrolment").</summary>
public sealed class LearnerService(ILearnerRepository learners, IUserDirectory users, ISisUnitOfWork unitOfWork)
{
    /// <summary>The Identity role a learner's account must hold (2.3).</summary>
    public const string LearnerRole = "Learner";

    private const int NameSearchLimit = 200;

    public async Task<PagedResponse<LearnerResponse>> ListAsync(LearnerListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var (sort, descending) = Sorting.Parse(query.Sort);

        IReadOnlyCollection<Guid>? matchingUsers = null;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            matchingUsers = await users.SearchIdsAsync(query.Search.Trim(), NameSearchLimit, cancellationToken);
        }

        var result = await learners.ListAsync(new LearnerQuery(page, pageSize, query.Search, matchingUsers, query.Status, sort, descending), cancellationToken);
        var usersById = await users.FindManyAsync(result.Items.Select(l => l.UserId), cancellationToken);

        // List rows omit the national identifier; it is available on single retrieval only.
        var items = result.Items
            .Select(l => ToResponse(l, usersById.TryGetValue(l.UserId, out var u) ? u : null, includeNationalId: false))
            .ToList();
        return new PagedResponse<LearnerResponse>(items, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<LearnerResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByIdAsync(id, cancellationToken);
        return learner is null
            ? Result.Failure<LearnerResponse>(SisErrors.LearnerNotFound)
            : Result.Success(ToResponse(learner, await users.FindAsync(learner.UserId, cancellationToken), includeNationalId: true));
    }

    public async Task<Result<LearnerResponse>> CreateAsync(CreateLearnerRequest request, CancellationToken cancellationToken)
    {
        // 13.5: the cross-module user reference is validated through the Identity contract at creation.
        var user = await users.FindAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<LearnerResponse>(SisErrors.UnknownUser("UserId"));
        }

        if (!user.Roles.Contains(LearnerRole, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<LearnerResponse>(SisErrors.UserLacksRole("UserId", LearnerRole));
        }

        if (await learners.UserIdExistsAsync(request.UserId, cancellationToken))
        {
            return Result.Failure<LearnerResponse>(SisErrors.LearnerUserAlreadyLinked);
        }

        if (await learners.LearnerNumberExistsAsync(request.LearnerNumber.Trim().ToUpperInvariant(), cancellationToken))
        {
            return Result.Failure<LearnerResponse>(SisErrors.LearnerNumberTaken);
        }

        var learner = Learner.Create(request.UserId, request.LearnerNumber, request.NationalId, request.DateOfBirth, request.Gender, request.Phone);
        learners.Add(learner);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(learner, user, includeNationalId: true));
    }

    public async Task<Result<LearnerResponse>> UpdateAsync(Guid id, UpdateLearnerRequest request, CancellationToken cancellationToken)
    {
        var learner = await learners.FindByIdAsync(id, cancellationToken);
        if (learner is null)
        {
            return Result.Failure<LearnerResponse>(SisErrors.LearnerNotFound);
        }

        learner.Amend(request.NationalId, request.DateOfBirth, request.Gender, request.Phone);
        learner.ChangeStatus(request.Status);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(learner, await users.FindAsync(learner.UserId, cancellationToken), includeNationalId: true));
    }

    /// <summary>Learner-role accounts that have no learner record yet, for the administrative interface.</summary>
    public async Task<IReadOnlyList<LearnerUserSummary>> ListUnlinkedLearnerUsersAsync(CancellationToken cancellationToken)
    {
        var candidates = await users.ListActiveInRoleAsync(LearnerRole, cancellationToken);
        var linked = await learners.GetLinkedUserIdsAsync(candidates.Select(c => c.Id), cancellationToken);
        return candidates.Where(c => !linked.Contains(c.Id)).Select(ToUser).ToList();
    }

    internal static LearnerResponse ToResponse(Learner l, UserSummary? user, bool includeNationalId) => new(
        l.Id,
        user is null ? null : ToUser(user),
        l.LearnerNumber,
        includeNationalId ? l.NationalId : null,
        l.DateOfBirth,
        l.Gender,
        l.Phone,
        l.Status,
        l.CreatedAtUtc,
        l.ModifiedAtUtc);

    internal static LearnerUserSummary ToUser(UserSummary u) => new(u.Id, u.UserName, u.Email, u.FullNameEn, u.FullNameAr, u.IsActive);
}
