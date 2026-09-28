using FluentValidation;
using OpenCampus.Identity.Application.Registration;

namespace OpenCampus.Identity.Application.Authentication;

// SDD 18.2: one validator per request contract; invoked centrally by the host, never by handlers.

public static class PasswordRules
{
    /// <summary>Raised from 8 to 12 with self-registration (Increment 7); the full rule set is <see cref="PasswordPolicy"/>.</summary>
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.UserNameOrEmail).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(PasswordRules.MaximumLength);
    }
}

public sealed class MfaVerifyRequestValidator : AbstractValidator<MfaVerifyRequest>
{
    public MfaVerifyRequestValidator()
    {
        RuleFor(r => r.Challenge).NotEmpty().MaximumLength(2048);
        RuleFor(r => r.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword).NotEmpty().MaximumLength(PasswordRules.MaximumLength);
        RuleFor(r => r.NewPassword)
            .NotEmpty()
            .Custom((password, context) =>
            {
                foreach (var message in PasswordPolicy.Validate(password))
                {
                    context.AddFailure(message);
                }
            })
            .NotEqual(r => r.CurrentPassword).WithMessage("The new password must differ from the current password.");
    }
}

public sealed class MfaConfirmRequestValidator : AbstractValidator<MfaConfirmRequest>
{
    public MfaConfirmRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}
