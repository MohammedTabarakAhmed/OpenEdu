using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.External;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Application.Registration;

/// <summary>
/// The six self-registration messages, each bilingual (English then Arabic, plain text) and dispatched through the
/// Identity EXT-01 contract. A message is a side effect of a committed change, never a condition of it: dispatch
/// outcomes are logged and never surface to the caller (same stance as the SIS <c>LearnerNotifier</c>). SEC-02: no
/// message ever carries a password, a hash or anything but the one-time verification link.
/// </summary>
public sealed class RegistrationNotifier(IEmailDispatcher email, IOptions<RegistrationOptions> options, ILogger<RegistrationNotifier> logger)
{
    public Task VerifyEmailAsync(User user, string token, CancellationToken cancellationToken)
    {
        var link = VerificationLink(token);
        return SendAsync("verify e-mail", user, new EmailMessage(
            [user.Email],
            "Verify your e-mail address — OpenCampus | تأكيد بريدك الإلكتروني",
            $"""
            Hello {user.FullNameEn},

            Thank you for creating an OpenCampus account ({user.UserName}). To confirm this e-mail address, open the link below and press "Verify my e-mail". The link works once and expires in {Hours()} hours.

            {link}

            If you did not create this account, ignore this message and nothing will happen.

            ----

            مرحباً {user.FullNameAr}،

            شكراً لإنشاء حساب في OpenCampus ‏({user.UserName}). لتأكيد هذا البريد الإلكتروني، افتح الرابط أدناه ثم اضغط «تأكيد بريدي الإلكتروني». يعمل الرابط مرة واحدة وتنتهي صلاحيته خلال {Hours()} ساعة.

            {link}

            إذا لم تنشئ هذا الحساب، تجاهل هذه الرسالة ولن يحدث شيء.
            """), cancellationToken);
    }

    /// <summary>Sent to the existing owner when someone registers with an address already in use (no enumeration through the API).</summary>
    public Task DuplicateEmailAsync(User existingUser, CancellationToken cancellationToken) =>
        SendAsync("duplicate e-mail", existingUser, new EmailMessage(
            [existingUser.Email],
            "Someone tried to register with your e-mail — OpenCampus | محاولة تسجيل ببريدك الإلكتروني",
            $"""
            Hello {existingUser.FullNameEn},

            Someone just tried to create a new OpenCampus account with this e-mail address. You already have an account ({existingUser.UserName}), so no new account was created and nothing has changed.

            If this was you, simply sign in with your existing account. If it was not, no action is needed.

            ----

            مرحباً {existingUser.FullNameAr}،

            حاول شخص ما للتو إنشاء حساب جديد في OpenCampus بهذا البريد الإلكتروني. لديك حساب بالفعل ({existingUser.UserName})، لذلك لم يُنشأ أي حساب جديد ولم يتغير شيء.

            إذا كنت أنت من حاول، فسجّل الدخول بحسابك الحالي. وإن لم تكن أنت، فلا حاجة لأي إجراء.
            """), cancellationToken);

    public Task LearnerWelcomeAsync(User user, CancellationToken cancellationToken) =>
        SendAsync("learner welcome", user, new EmailMessage(
            [user.Email],
            "Your OpenCampus account is ready | حسابك في OpenCampus جاهز",
            $"""
            Hello {user.FullNameEn},

            Your e-mail address is verified and your learner account ({user.UserName}) is active. Sign in at {SignInLink()} to browse the course catalogue and enrol.

            ----

            مرحباً {user.FullNameAr}،

            تم تأكيد بريدك الإلكتروني وأصبح حساب المتعلّم الخاص بك ({user.UserName}) نشطاً. سجّل الدخول عبر {SignInLink()} لتصفّح دليل المقررات والتسجيل فيها.
            """), cancellationToken);

    public Task AwaitingApprovalAsync(User user, CancellationToken cancellationToken) =>
        SendAsync("awaiting approval", user, new EmailMessage(
            [user.Email],
            "E-mail verified — your request is awaiting approval | تم التأكيد — طلبك بانتظار الموافقة",
            $"""
            Hello {user.FullNameEn},

            Your e-mail address is verified. Because you asked for a {user.RequestedRole} account, an administrator must approve the request before you can sign in. You will receive another message when a decision has been made.

            ----

            مرحباً {user.FullNameAr}،

            تم تأكيد بريدك الإلكتروني. نظراً لأنك طلبت حساباً بصفة {user.RequestedRole}، يجب أن يوافق مسؤول النظام على الطلب قبل أن تتمكن من تسجيل الدخول. ستصلك رسالة أخرى عند اتخاذ القرار.
            """), cancellationToken);

    public Task ApprovedAsync(User user, string roleName, CancellationToken cancellationToken) =>
        SendAsync("registration approved", user, new EmailMessage(
            [user.Email],
            "Your OpenCampus account has been approved | تمت الموافقة على حسابك في OpenCampus",
            $"""
            Hello {user.FullNameEn},

            An administrator has approved your request. Your account ({user.UserName}) now holds the {roleName} role and is active. Sign in at {SignInLink()}.

            ----

            مرحباً {user.FullNameAr}،

            وافق مسؤول النظام على طلبك. يحمل حسابك ({user.UserName}) الآن صفة {roleName} وهو نشط. سجّل الدخول عبر {SignInLink()}.
            """), cancellationToken);

    public Task RejectedAsync(User user, CancellationToken cancellationToken) =>
        SendAsync("registration rejected", user, new EmailMessage(
            [user.Email],
            "Your OpenCampus account request was not approved | لم تتم الموافقة على طلب حسابك",
            $"""
            Hello {user.FullNameEn},

            An administrator has reviewed your request for a {user.RequestedRole} account ({user.UserName}) and did not approve it. The request has been removed. If you believe this is a mistake, contact the institution's administrator.

            ----

            مرحباً {user.FullNameAr}،

            راجع مسؤول النظام طلبك للحصول على حساب بصفة {user.RequestedRole} ‏({user.UserName}) ولم يوافق عليه. تمت إزالة الطلب. إذا كنت تعتقد أن هذا خطأ، فتواصل مع مسؤول المؤسسة.
            """), cancellationToken);

    public string VerificationLink(string token) =>
        $"{options.Value.ClientBaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(token)}";

    private string SignInLink() => $"{options.Value.ClientBaseUrl.TrimEnd('/')}/login";

    private int Hours() => (int)Math.Round(options.Value.VerificationLifetime.TotalHours);

    private async Task SendAsync(string notice, User user, EmailMessage message, CancellationToken cancellationToken)
    {
        // The subject of the log line is the user identifier, never the address (SEC-02: minimal personal data in logs).
        var result = await email.SendAsync(message, cancellationToken);
        switch (result.Outcome)
        {
            case ExternalOutcome.Succeeded:
                logger.LogInformation("Registration notice '{Notice}' for user {UserId} dispatched ({Reference})", notice, user.Id, result.Reference);
                break;
            default:
                logger.LogWarning("Registration notice '{Notice}' for user {UserId} not dispatched: {Reasons}", notice, user.Id, string.Join("; ", result.Failures.Select(f => f.Reason)));
                break;
        }
    }
}
