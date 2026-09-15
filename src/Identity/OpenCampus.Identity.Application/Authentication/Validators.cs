using FluentValidation;

namespace OpenCampus.Identity.Application.Authentication;

// SDD 18.2: one validator per request contract; invoked centrally by the host, never by handlers.

public static class PasswordRules
{
    public const int MinimumLength = 8;
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
            .MinimumLength(PasswordRules.MinimumLength)
            .MaximumLength(PasswordRules.MaximumLength)
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
