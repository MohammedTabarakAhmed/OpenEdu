using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Permissions;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Identity.Domain;

public class RoleAndPermissionTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Role_Create_RequiresName()
    {
        Should.Throw<DomainException>(() => Role.Create(" ", "desc"));
    }

    [Fact]
    public void Role_GrantPermission_IsIdempotent()
    {
        var role = Role.Create("Administrator", "Full authority");
        var permissionId = Guid.NewGuid();

        role.GrantPermission(permissionId).ShouldBeTrue();
        role.GrantPermission(permissionId).ShouldBeFalse();

        role.Permissions.Count.ShouldBe(1);
        role.Permissions.Single().RoleId.ShouldBe(role.Id);
    }

    [Fact]
    public void Role_RevokePermission_ReportsWhetherGrantExisted()
    {
        var role = Role.Create("Administrator", "Full authority");
        var permissionId = Guid.NewGuid();
        role.GrantPermission(permissionId);

        role.RevokePermission(permissionId).ShouldBeTrue();
        role.RevokePermission(permissionId).ShouldBeFalse();
    }

    // Appendix C: codes have the form module.resource.action.
    [Theory]
    [InlineData("identity.user.read")]
    [InlineData("sis.grade.release")]
    public void Permission_Create_AcceptsThreePartCode(string code)
    {
        Permission.Create(code, "desc").Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("user.read")]
    [InlineData("identity.user.read.all")]
    [InlineData("identity..read")]
    public void Permission_Create_RejectsMalformedCode(string code)
    {
        Should.Throw<DomainException>(() => Permission.Create(code, "desc"));
    }

    // SEC-31: an audit record captures actor, event type, affected entity, timestamp and address.
    [Fact]
    public void AuditEvent_Record_CapturesRequiredAttributes()
    {
        var actor = Guid.NewGuid();
        var entity = Guid.NewGuid();

        var audit = AuditEvent.Record(AuditEventTypes.RoleAssigned, "User", entity, actor, Now, "192.168.0.1", "{\"role\":\"Learner\"}");

        audit.Id.ShouldNotBe(Guid.Empty);
        audit.EventType.ShouldBe(AuditEventTypes.RoleAssigned);
        audit.EntityName.ShouldBe("User");
        audit.EntityId.ShouldBe(entity);
        audit.UserId.ShouldBe(actor);
        audit.OccurredAtUtc.ShouldBe(Now);
        audit.IpAddress.ShouldBe("192.168.0.1");
        audit.DetailsJson.ShouldBe("{\"role\":\"Learner\"}");
    }

    [Fact]
    public void AuditEvent_Record_RejectsMissingTypeOrEntity()
    {
        Should.Throw<DomainException>(() => AuditEvent.Record("", "User", null, null, Now, null));
        Should.Throw<DomainException>(() => AuditEvent.Record(AuditEventTypes.AuthenticationFailed, "", null, null, Now, null));
    }
}
