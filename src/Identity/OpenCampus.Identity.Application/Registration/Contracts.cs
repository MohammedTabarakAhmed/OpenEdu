using FluentValidation;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Application.Registration;

// Requests (API-02, SEC-18) for self-registration (Increment 7).

/// <summary>What the person is creating the account for; one of <see cref="RoleNames"/>. Recorded, never granted, until approval.</summary>
public sealed record RegisterRequest(
    string AccountType,
    string UserName,
    string Email,
    string Password,
    string FullNameEn,
    string FullNameAr);

public sealed record VerifyEmailRequest(string Token);

public sealed record ResendVerificationRequest(string Email);

// Responses.

/// <summary>Always the same body whether or not an account was created, so the e-mail directory cannot be enumerated.</summary>
public sealed record RegistrationAcceptedResponse(string Message)
{
    public const string CheckInbox = "registration.check_inbox";

    public static readonly RegistrationAcceptedResponse Default = new(CheckInbox);
}

/// <summary>What happened on verification: a learner is <c>Activated</c>; staff are <c>AwaitingApproval</c>.</summary>
public sealed record VerifyEmailResponse(string Outcome, string RequestedRole)
{
    public const string Activated = "Activated";
    public const string AwaitingApproval = "AwaitingApproval";
}

// Validators (SDD 18.2).

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public const string TokenPattern = "^[A-Za-z0-9_-]+$";

    public RegisterRequestValidator()
    {
        RuleFor(r => r.AccountType)
            .NotEmpty()
            .Must(t => RoleNames.All.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Account type must be one of: Learner, Instructor, Registrar, Administrator.");
        RuleFor(r => r.UserName).NotEmpty().MaximumLength(User.UserNameMaxLength).Matches("^[A-Za-z0-9._-]+$");
        RuleFor(r => r.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
        RuleFor(r => r.Password).NotEmpty().Custom((password, context) =>
        {
            foreach (var message in PasswordPolicy.Validate(password, context.InstanceToValidate.UserName, context.InstanceToValidate.Email))
            {
                context.AddFailure(message);
            }
        });
        RuleFor(r => r.FullNameEn).NotEmpty().MaximumLength(User.FullNameMaxLength);
        RuleFor(r => r.FullNameAr).NotEmpty().MaximumLength(User.FullNameMaxLength);
    }
}

public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().MaximumLength(User.VerificationTokenHashMaxLength).Matches(RegisterRequestValidator.TokenPattern);
    }
}

public sealed class ResendVerificationRequestValidator : AbstractValidator<ResendVerificationRequest>
{
    public ResendVerificationRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
    }
}
