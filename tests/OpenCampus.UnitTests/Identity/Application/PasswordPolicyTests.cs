using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Application.Registration;

namespace OpenCampus.UnitTests.Identity.Application;

public class PasswordPolicyTests
{
    [Fact]
    public void MinimumLengthIsTwelve()
    {
        PasswordRules.MinimumLength.ShouldBe(12);
        PasswordPolicy.Validate("Eleven-chars").ShouldBeEmpty();
        PasswordPolicy.Validate("Eleven-char").ShouldHaveSingleItem().ShouldContain("at least 12");
        PasswordPolicy.Validate(new string('x', 129)).ShouldHaveSingleItem().ShouldContain("128");
        PasswordPolicy.Validate(new string('x', 128)).ShouldBeEmpty();
    }

    [Fact]
    public void RefusesPasswordsContainingUserNameOrEmailLocalPart()
    {
        PasswordPolicy.Validate("xxAda.Lovelacexx-1", "ada.lovelace", "other@example.org").ShouldHaveSingleItem().ShouldContain("user name");
        PasswordPolicy.Validate("prefix-ADA1815-suffix", "someone", "ada1815@example.org").ShouldHaveSingleItem().ShouldContain("e-mail");
        PasswordPolicy.Validate("prefix-ada-suffix-long", "ada", "ada@example.org").ShouldBeEmpty(); // fragments under 4 chars are ignored
        PasswordPolicy.Validate("Correct-Horse-Battery-Staple-1", "ada.lovelace", "ada@example.org").ShouldBeEmpty();
    }

    [Fact]
    public void RefusesCommonPasswordsCaseInsensitively()
    {
        CommonPasswords.Count.ShouldBe(994); // 1 000 lines; six differ only by case and fold into one entry each.
        CommonPasswords.Contains("qwerty123456").ShouldBeTrue();
        PasswordPolicy.Validate("qwerty123456").ShouldHaveSingleItem().ShouldContain("too common");
        PasswordPolicy.Validate("QWERTY123456").ShouldHaveSingleItem().ShouldContain("too common");
        PasswordPolicy.Validate("Not-A-Common-Password-2026").ShouldBeEmpty();
    }

    [Fact]
    public void AllCharactersAreAllowed()
    {
        PasswordPolicy.Validate("كلمة مرور طويلة جداً 🙂").ShouldBeEmpty();
    }

    [Fact]
    public void ValidatorsReportFailuresUnderThePasswordProperty()
    {
        var register = new RegisterRequestValidator().Validate(new RegisterRequest("Learner", "ada.lovelace", "ada@example.org", "ada.lovelace-1", "Ada", "آدا"));
        register.IsValid.ShouldBeFalse();
        register.Errors.ShouldAllBe(e => e.PropertyName == "Password");

        var create = new CreateUserRequestValidator().Validate(new CreateUserRequest("ada", "ada@example.org", "qwerty123456", "Ada", "آدا", ["Learner"]));
        create.Errors.ShouldAllBe(e => e.PropertyName == "Password");

        var change = new ChangePasswordRequestValidator().Validate(new ChangePasswordRequest("old-password-value", "short"));
        change.Errors.ShouldAllBe(e => e.PropertyName == "NewPassword");
    }

    [Fact]
    public void RegisterRequestValidator_ChecksAccountTypeUserNameEmailAndToken()
    {
        var validator = new RegisterRequestValidator();
        validator.Validate(new RegisterRequest("Owner", "ada", "ada@example.org", "Correct-Horse-Battery-Staple-1", "Ada", "آدا")).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("AccountType");
        validator.Validate(new RegisterRequest("learner", "ada", "ada@example.org", "Correct-Horse-Battery-Staple-1", "Ada", "آدا")).IsValid.ShouldBeTrue();
        validator.Validate(new RegisterRequest("Learner", "ada lovelace", "ada@example.org", "Correct-Horse-Battery-Staple-1", "Ada", "آدا")).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("UserName");
        validator.Validate(new RegisterRequest("Learner", "ada", "not-an-email", "Correct-Horse-Battery-Staple-1", "Ada", "آدا")).Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("Email");

        new VerifyEmailRequestValidator().Validate(new VerifyEmailRequest("abc/def")).IsValid.ShouldBeFalse();
        new VerifyEmailRequestValidator().Validate(new VerifyEmailRequest("abc_DEF-123")).IsValid.ShouldBeTrue();
        new ResendVerificationRequestValidator().Validate(new ResendVerificationRequest("nope")).IsValid.ShouldBeFalse();
    }
}
