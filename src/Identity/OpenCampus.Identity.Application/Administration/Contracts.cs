using FluentValidation;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Application.Administration;

// Requests (API-02, SEC-18).

public sealed record CreateUserRequest(
    string UserName,
    string Email,
    string Password,
    string FullNameEn,
    string FullNameAr,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(string Email, string FullNameEn, string FullNameAr);

/// <summary>Replaces the user's role set with the one supplied.</summary>
public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Collection parameters (API-03). Sort accepts userName, email, fullNameEn, createdAtUtc; prefix with '-' for descending.</summary>
public sealed record UserListQuery(int? Page, int? PageSize, string? Search, bool? IsActive, string? Sort);

public sealed record AuditListQuery(int? Page, int? PageSize, Guid? UserId, string? EventType, DateTime? FromUtc, DateTime? ToUtc);

// Responses.

public sealed record UserResponse(
    Guid Id,
    string UserName,
    string Email,
    string FullNameEn,
    string FullNameAr,
    bool IsActive,
    bool MfaEnabled,
    DateTime? LockedUntilUtc,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed record RoleResponse(Guid Id, string Name, string Description, IReadOnlyList<string> Permissions);

public sealed record SessionResponse(
    Guid Id,
    Guid FamilyId,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc,
    string? IpAddress,
    string? UserAgent);

public sealed record AuditEventResponse(
    Guid Id,
    Guid? UserId,
    string EventType,
    string EntityName,
    Guid? EntityId,
    string? DetailsJson,
    DateTime OccurredAtUtc,
    string? IpAddress);

// Validators (SDD 18.2).

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.UserName).NotEmpty().MaximumLength(User.UserNameMaxLength).Matches("^[A-Za-z0-9._-]+$");
        RuleFor(r => r.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
        RuleFor(r => r.Password).NotEmpty().MinimumLength(PasswordRules.MinimumLength).MaximumLength(PasswordRules.MaximumLength);
        RuleFor(r => r.FullNameEn).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(r => r.FullNameAr).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(r => r.Roles).NotNull();
        RuleForEach(r => r.Roles).NotEmpty();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
        RuleFor(r => r.FullNameEn).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(r => r.FullNameAr).NotEmpty().MaximumLength(User.FullNameMaxLength);
    }
}

public sealed class AssignRolesRequestValidator : AbstractValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator()
    {
        RuleFor(r => r.Roles).NotNull();
        RuleForEach(r => r.Roles).NotEmpty();
    }
}

public sealed class UserListQueryValidator : AbstractValidator<UserListQuery>
{
    public UserListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, SharedKernel.Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Sort)
            .Must(s => s is null || UserAdministrationService.SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Sort must be one of: userName, email, fullNameEn, createdAtUtc (prefix '-' for descending).");
    }
}

public sealed class AuditListQueryValidator : AbstractValidator<AuditListQuery>
{
    public AuditListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, SharedKernel.Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.EventType).MaximumLength(64);
        RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc < q.ToUtc)
            .WithMessage("FromUtc must precede ToUtc.");
    }
}
