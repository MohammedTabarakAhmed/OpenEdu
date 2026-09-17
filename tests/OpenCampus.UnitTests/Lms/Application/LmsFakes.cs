using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Assessment;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.UnitTests.Lms.Application;

/// <summary>In-memory collaborators so the SEC-12 scope logic and upload controls are tested without a database or disk.</summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public HashSet<string> Permissions { get; } = [];

    public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
}

internal sealed class FakeSectionAccess : ISectionAccess
{
    public Dictionary<Guid, SectionSummary> Sections { get; } = [];

    public HashSet<(Guid UserId, Guid SectionId)> Enrolments { get; } = [];

    public SectionSummary AddSection(Guid instructorUserId)
    {
        var id = Guid.NewGuid();
        var section = new SectionSummary(id, "CS101-A", "2026 Autumn", "Open", Guid.NewGuid(), "CS101", "Programming", "برمجة", instructorUserId);
        Sections[id] = section;
        return section;
    }

    public Task<SectionSummary?> FindSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sections.GetValueOrDefault(sectionId));

    public Task<bool> IsLearnerEnrolledAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Enrolments.Contains((userId, sectionId)));

    public Task<IReadOnlyList<SectionSummary>> ListSectionsForInstructorAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SectionSummary>>(Sections.Values.Where(s => s.InstructorUserId == userId).ToList());

    public Task<IReadOnlyList<SectionSummary>> ListSectionsForLearnerAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SectionSummary>>(Enrolments.Where(e => e.UserId == userId).Select(e => Sections[e.SectionId]).ToList());

    public Task<IReadOnlyList<SectionSummary>> ListLiveSectionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SectionSummary>>(Sections.Values.ToList());

    public List<SessionSummary> Sessions { get; } = [];

    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SessionSummary>>(Sessions.Where(s => s.SectionId == sectionId).OrderBy(s => s.ScheduledStartUtc).ToList());

    public Task<IReadOnlyList<EnrolledLearner>> ListEnrolledLearnersAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EnrolledLearner>>(Enrolments.Where(e => e.SectionId == sectionId)
            .Select(e => new EnrolledLearner(e.UserId, "L" + e.UserId.ToString("N")[..6].ToUpperInvariant(), "Learner", "متعلم")).ToList());
}

internal sealed class FakeContentRepository : ICourseContentRepository, ILmsUnitOfWork
{
    public List<CourseContent> Items { get; } = [];

    public int SaveCount { get; private set; }

    public Task<CourseContent?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(c => c.Id == id && !c.IsDeleted));

    public Task<CourseContent?> FindByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(c => !c.IsDeleted && c.AllResources.Any(r => r.Id == resourceId)));

    public Task<IReadOnlyList<CourseContent>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CourseContent>>(Items.Where(c => c.SectionId == sectionId && !c.IsDeleted).OrderBy(c => c.SortOrder).ToList());

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Count > 0);

    public void Add(CourseContent content) => Items.Add(content);

    public void Remove(CourseContent content) => content.MarkDeleted();

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileStore : IFileStore
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<StoredFile?> SaveAsync(string relativeDirectory, string extension, Stream content, long maxSizeBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length == 0 || bytes.Length > maxSizeBytes)
        {
            return Task.FromResult<StoredFile?>(null);
        }

        var path = $"{relativeDirectory}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        Files[path] = bytes;
        return Task.FromResult<StoredFile?>(new StoredFile(path, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(Files.TryGetValue(relativePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        Files.Remove(relativePath);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAssignmentRepository : IAssignmentRepository
{
    public List<Assignment> Items { get; } = [];

    public Task<Assignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(a => a.Id == id && !a.IsDeleted));

    public Task<Assignment?> FindBySubmissionIdAsync(Guid submissionId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(a => !a.IsDeleted && a.Submissions.Any(s => s.Id == submissionId)));

    public Task<IReadOnlyList<Assignment>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Assignment>>(Items.Where(a => a.SectionId == sectionId && !a.IsDeleted).OrderBy(a => a.DueAtUtc).ToList());

    public Task<IReadOnlyList<Assignment>> ListPublishedBySectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken cancellationToken)
    {
        var set = sectionIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<Assignment>>(Items.Where(a => set.Contains(a.SectionId) && a.IsPublished && !a.IsDeleted).ToList());
    }

    public void Add(Assignment assignment) => Items.Add(assignment);

    public void Remove(Assignment assignment) => assignment.MarkDeleted();
}

internal sealed class FakeAttendanceRepository : IAttendanceRepository
{
    public List<AttendanceRecord> Items { get; } = [];

    public Task<AttendanceRecord?> FindAsync(Guid sessionId, Guid learnerUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(r => r.SessionId == sessionId && r.LearnerUserId == learnerUserId));

    public Task<IReadOnlyList<AttendanceRecord>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AttendanceRecord>>(Items.Where(r => r.SessionId == sessionId).ToList());

    public Task<IReadOnlyList<AttendanceRecord>> ListBySessionsAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var set = sessionIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<AttendanceRecord>>(Items.Where(r => set.Contains(r.SessionId)).ToList());
    }

    public void Add(AttendanceRecord record) => Items.Add(record);
}

internal sealed class FakeAssessmentOutcomes : IAssessmentOutcomes
{
    public List<(Guid SectionId, Guid LearnerUserId, decimal Percent)> Reports { get; } = [];

    public Task ReportAttendanceRateAsync(Guid sectionId, Guid learnerUserId, decimal attendancePercent, CancellationToken cancellationToken)
    {
        Reports.Add((sectionId, learnerUserId, attendancePercent));
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
}

/// <summary>A wired-up LMS application with one section: an assigned instructor, another instructor, an enrolled learner, a stranger and an administrator.</summary>
internal sealed class LmsHarness
{
    public static readonly Guid AssignedInstructor = Guid.NewGuid();
    public static readonly Guid OtherInstructor = Guid.NewGuid();
    public static readonly Guid EnrolledLearnerId = Guid.NewGuid();
    public static readonly Guid OtherLearner = Guid.NewGuid();
    public static readonly Guid Administrator = Guid.NewGuid();

    public FakeCurrentUser User { get; } = new();

    public FakeSectionAccess Access { get; } = new();

    public FakeContentRepository Repository { get; } = new();

    public FakeFileStore Store { get; } = new();

    public StorageOptions Storage { get; } = new() { RootPath = "data/files", MaxUploadSizeBytes = 1024, PermittedExtensions = [".pdf", ".txt"] };

    public SectionSummary Section { get; }

    public ContentService Content { get; }

    public ResourceService Resources { get; }

    public FakeAssignmentRepository AssignmentRepository { get; } = new();

    public FakeAttendanceRepository AttendanceRepository { get; } = new();

    public FakeAssessmentOutcomes Outcomes { get; } = new();

    public FakeClock Clock { get; } = new(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));

    public AssignmentService Assignments { get; }

    public AttendanceService Attendance { get; }

    public LmsHarness()
    {
        Section = Access.AddSection(AssignedInstructor);
        Access.Enrolments.Add((EnrolledLearnerId, Section.Id));
        var scopes = new SectionScopeResolver(Access, User);
        Content = new ContentService(Repository, Access, User, scopes, Store, Options.Create(Storage), Repository);
        Resources = new ResourceService(Repository, scopes, Store);
        Assignments = new AssignmentService(AssignmentRepository, Access, User, scopes, Store, Options.Create(Storage), Repository, Clock);
        Attendance = new AttendanceService(AttendanceRepository, Access, Outcomes, User, scopes, Repository, Clock);
    }

    public LmsHarness As(Guid userId, params string[] permissions)
    {
        User.UserId = userId;
        User.Permissions.Clear();
        foreach (var permission in permissions)
        {
            User.Permissions.Add(permission);
        }

        return this;
    }

    public LmsHarness AsAdministrator() => As(Administrator, KnownPermissions.SectionAdministration);

    /// <summary>Seeds, as the assigned instructor, one unit with a published page, an unpublished page and a published file item with one resource.</summary>
    public async Task<(CourseContent Unit, ContentItem Published, ContentItem Draft, ContentItem FileItem, Resource Resource)> SeedAsync()
    {
        var unit = CourseContent.Create(Section.Id, "Week 1", "الأسبوع 1", 0);
        var published = unit.AddItem("Notes", "ملاحظات", ContentItemType.Page, "Read me");
        unit.PublishItem(published.Id);
        var draft = unit.AddItem("Draft", "مسودة", ContentItemType.Page, "Not yet");
        var fileItem = unit.AddItem("Slides", "شرائح", ContentItemType.File, null);
        var stored = (await Store.SaveAsync($"content/{Section.Id:N}/{fileItem.Id:N}", ".pdf", new MemoryStream("%PDF"u8.ToArray()), 1024, CancellationToken.None))!;
        var resource = unit.AttachResource(fileItem.Id, "slides.pdf", stored.RelativePath, "application/pdf", stored.SizeBytes, stored.ContentHash);
        unit.PublishItem(fileItem.Id);
        Repository.Add(unit);
        return (unit, published, draft, fileItem, resource);
    }

    public static FileUpload Upload(string fileName, int size, string contentType = "application/pdf") =>
        new(fileName, contentType, size, new MemoryStream(new byte[size]));
}
