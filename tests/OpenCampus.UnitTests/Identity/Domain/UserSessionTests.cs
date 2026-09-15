using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Identity.Domain;

public class UserSessionTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);
    private static readonly Guid UserId = Guid.NewGuid();

    private static UserSession Start() =>
        UserSession.Start(UserId, "hash-1", Now, Lifetime, "127.0.0.1", "agent");

    [Fact]
    public void Start_CreatesUsableSessionThatIsItsOwnFamilyRoot()
    {
        var session = Start();

        session.UserId.ShouldBe(UserId);
        session.FamilyId.ShouldBe(session.Id);
        session.RefreshTokenHash.ShouldBe("hash-1");
        session.IssuedAtUtc.ShouldBe(Now);
        session.ExpiresAtUtc.ShouldBe(Now.Add(Lifetime));
        session.IsRevoked.ShouldBeFalse();
        session.IsUsable(Now).ShouldBeTrue();
    }

    [Fact]
    public void Start_RejectsEmptyHashOrNonPositiveLifetime()
    {
        Should.Throw<DomainException>(() => UserSession.Start(UserId, "", Now, Lifetime, null, null));
        Should.Throw<DomainException>(() => UserSession.Start(UserId, "hash", Now, TimeSpan.Zero, null, null));
    }

    [Fact]
    public void Start_TruncatesOverlongClientMetadata()
    {
        var longAgent = new string('x', UserSession.UserAgentMaxLength + 50);

        var session = UserSession.Start(UserId, "hash", Now, Lifetime, null, longAgent);

        session.UserAgent!.Length.ShouldBe(UserSession.UserAgentMaxLength);
    }

    [Fact]
    public void IsUsable_FalseOnceExpired()
    {
        var session = Start();

        session.IsUsable(Now.Add(Lifetime)).ShouldBeFalse();
    }

    // SEC-07: refresh rotates the credential and invalidates its predecessor.
    [Fact]
    public void Rotate_RevokesPredecessorAndKeepsFamily()
    {
        var first = Start();
        var later = Now.AddMinutes(10);

        var second = first.Rotate("hash-2", later, Lifetime, "10.0.0.1", "agent-2");

        first.IsRevoked.ShouldBeTrue();
        first.RevokedAtUtc.ShouldBe(later);
        first.IsUsable(later).ShouldBeFalse();

        second.Id.ShouldNotBe(first.Id);
        second.FamilyId.ShouldBe(first.FamilyId);
        second.UserId.ShouldBe(UserId);
        second.RefreshTokenHash.ShouldBe("hash-2");
        second.IssuedAtUtc.ShouldBe(later);
        second.ExpiresAtUtc.ShouldBe(later.Add(Lifetime));
        second.IsUsable(later).ShouldBeTrue();
    }

    [Fact]
    public void Rotate_RefusedForRevokedSession()
    {
        var session = Start();
        session.Revoke(Now);

        Should.Throw<DomainException>(() => session.Rotate("hash-2", Now, Lifetime, null, null));
    }

    [Fact]
    public void Rotate_RefusedForExpiredSession()
    {
        var session = Start();

        Should.Throw<DomainException>(() => session.Rotate("hash-2", Now.Add(Lifetime), Lifetime, null, null));
    }

    // SEC-08: revocation is immediate and sticky.
    [Fact]
    public void Revoke_IsIdempotentAndKeepsFirstTimestamp()
    {
        var session = Start();

        session.Revoke(Now);
        session.Revoke(Now.AddHours(1));

        session.RevokedAtUtc.ShouldBe(Now);
        session.IsUsable(Now).ShouldBeFalse();
    }
}
