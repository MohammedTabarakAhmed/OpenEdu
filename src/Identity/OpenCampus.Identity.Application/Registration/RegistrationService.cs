using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Application.Registration;

/// <summary>
/// Self-registration (Increment 7, beyond the SDD): create → verify e-mail → (learner) activate or (staff) await an
/// administrator. Every path is anonymous, so the service is written not to leak whether an address is known: the
/// password is hashed before any lookup (uniform timing), a duplicate address gets the same 202 as a new one, and
/// unknown, consumed and expired tokens share one answer. The requested role is stored as text and granted only by
/// <see cref="User.ApproveRegistration"/>; a learner's approval is automatic on verification, everyone else's is an
/// administrator's decision (SEC-10/11). Audit events extend SEC-30; the actor is null because nobody is signed in.
/// </summary>
public sealed class RegistrationService(
    IUserRepository users,
    IRoleRepository roles,
    IAuditEventRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IRefreshTokenGenerator tokens,
    RegistrationNotifier notifier,
    ILearnerRecordProvisioner learnerRecords,
    IOptions<RegistrationOptions> options,
    TimeProvider clock,
    ILogger<RegistrationService> logger)
{
    public async Task<Result<RegistrationAcceptedResponse>> RegisterAsync(RegisterRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Result.Failure<RegistrationAcceptedResponse>(RegistrationErrors.Disabled);
        }

        // Hash first so a request for a known address costs the same as one for a new address.
        var passwordHash = passwordHasher.Hash(request.Password);
        var now = clock.GetUtcNow().UtcDateTime;
        var userName = request.UserName.Trim();
        var email = request.Email.Trim();
        var requestedRole = RoleNames.Normalise(request.AccountType)
            ?? throw new InvalidOperationException("Account type was not validated."); // The validator guarantees membership.

        var sameUserName = await users.FindByUserNameOrEmailAsync(userName, cancellationToken);
        if (sameUserName is not null)
        {
            if (!sameUserName.IsRegistrationExpired(now))
            {
                return Result.Failure<RegistrationAcceptedResponse>(RegistrationErrors.UserNameTaken);
            }

            users.Remove(sameUserName); // A stale, never-verified registration gives up its user name.
        }

        var sameEmail = await users.FindByEmailAsync(email, cancellationToken);
        if (sameEmail is not null && sameEmail.Id != sameUserName?.Id)
        {
            if (!sameEmail.IsRegistrationExpired(now))
            {
                // The address belongs to someone: tell them, not the caller, and answer exactly as for a new address.
                audit.Add(AuditEvent.Record(AuditEventTypes.RegistrationDuplicateEmail, nameof(User), sameEmail.Id, null, now, client.IpAddress));
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await notifier.DuplicateEmailAsync(sameEmail, cancellationToken);
                return Result.Success(RegistrationAcceptedResponse.Default);
            }

            users.Remove(sameEmail);
        }

        var user = User.Register(email, userName, passwordHash, request.FullNameEn, request.FullNameAr, requestedRole);
        var token = tokens.Generate();
        user.IssueVerificationToken(token.Hash, now, options.Value.VerificationLifetime, options.Value.ResendCooldown);

        users.Add(user);
        audit.Add(AuditEvent.Record(
            AuditEventTypes.UserRegistered, nameof(User), user.Id, null, now, client.IpAddress,
            JsonSerializer.Serialize(new { requestedRole })));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.VerifyEmailAsync(user, token.Value, cancellationToken);
        return Result.Success(RegistrationAcceptedResponse.Default);
    }

    public async Task<Result<VerifyEmailResponse>> VerifyEmailAsync(VerifyEmailRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Result.Failure<VerifyEmailResponse>(RegistrationErrors.Disabled);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.FindByVerificationTokenHashAsync(tokens.Hash(request.Token), cancellationToken);
        if (user is null || !user.IsVerificationTokenUsable(now))
        {
            return Result.Failure<VerifyEmailResponse>(RegistrationErrors.LinkInvalid);
        }

        user.ConfirmEmail(now);
        audit.Add(AuditEvent.Record(AuditEventTypes.EmailVerified, nameof(User), user.Id, null, now, client.IpAddress));

        if (user.RequestedRole != RoleNames.Learner)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await notifier.AwaitingApprovalAsync(user, cancellationToken);
            return Result.Success(new VerifyEmailResponse(VerifyEmailResponse.AwaitingApproval, user.RequestedRole!));
        }

        // Learner: self-service — approval is automatic and the SIS learner record follows the committed activation.
        var role = await roles.FindByNameAsync(RoleNames.Learner, cancellationToken)
            ?? throw new InvalidOperationException("The Learner role has not been provisioned.");
        user.ApproveRegistration(role.Id);
        audit.Add(AuditEvent.Record(AuditEventTypes.RoleAssigned, nameof(User), user.Id, null, now, client.IpAddress, JsonSerializer.Serialize(new { role = role.Name })));
        audit.Add(AuditEvent.Record(AuditEventTypes.RegistrationApproved, nameof(User), user.Id, null, now, client.IpAddress, JsonSerializer.Serialize(new { requestedRole = user.RequestedRole, selfService = true })));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await EnsureLearnerRecordAsync(user.Id, cancellationToken);
        await notifier.LearnerWelcomeAsync(user, cancellationToken);
        return Result.Success(new VerifyEmailResponse(VerifyEmailResponse.Activated, user.RequestedRole!));
    }

    /// <summary>Always succeeds: the caller cannot tell whether the address is registered, pending, or unknown.</summary>
    public async Task<Result> ResendVerificationAsync(ResendVerificationRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return Result.Failure(RegistrationErrors.Disabled);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.FindByEmailAsync(request.Email.Trim(), cancellationToken);
        if (user is null || user.RegistrationStatus != RegistrationStatus.AwaitingVerification)
        {
            return Result.Success();
        }

        if (user.VerificationTokenIssuedAtUtc is { } issued && issued.Add(options.Value.ResendCooldown) > now)
        {
            logger.LogInformation("Verification resend for user {UserId} skipped: inside the cooldown", user.Id);
            return Result.Success();
        }

        var token = tokens.Generate();
        user.IssueVerificationToken(token.Hash, now, options.Value.VerificationLifetime, options.Value.ResendCooldown);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.VerifyEmailAsync(user, token.Value, cancellationToken);
        return Result.Success();
    }

    /// <summary>The Identity change is already committed; a SIS failure is logged and recovered from the Learners screen (unlinked users), never surfaced.</summary>
    internal async Task EnsureLearnerRecordAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await learnerRecords.EnsureAsync(userId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Learner record could not be provisioned for user {UserId}; link the account from the Learners screen", userId);
        }
    }
}
