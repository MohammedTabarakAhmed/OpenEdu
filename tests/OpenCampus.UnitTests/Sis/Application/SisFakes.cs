using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.UnitTests.Sis.Application;

/// <summary>In-memory collaborators so certificate issuance, scope and verification are tested without a database, disk or PDF library.</summary>
internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public HashSet<string> Permissions { get; } = [];

    public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
}

internal sealed class FakeAuditTrail : IAuditTrail
{
    public List<(string EventType, string EntityName, Guid EntityId, string? Details)> Events { get; } = [];

    public Task RecordAsync(string eventType, string entityName, Guid entityId, string? detailsJson, CancellationToken cancellationToken)
    {
        Events.Add((eventType, entityName, entityId, detailsJson));
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    public Dictionary<Guid, UserSummary> Users { get; } = [];

    public UserSummary Add(Guid id, string nameEn, string nameAr)
    {
        var user = new UserSummary(id, nameEn.ToLowerInvariant().Replace(' ', '.'), $"{id:N}@example.test", nameEn, nameAr, true, ["Learner"]);
        Users[id] = user;
        return user;
    }

    public Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Users.GetValueOrDefault(userId));

    public Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, UserSummary>>(userIds.Distinct().Where(Users.ContainsKey).ToDictionary(id => id, id => Users[id]));

    public Task<IReadOnlyList<UserSummary>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(Users.Values.ToList());

    public Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
}

/// <summary>Reference data behind a certificate; only the lookups the certificate service uses are implemented.</summary>
internal sealed class FakeStructure : IProgrammeRepository, ICourseRepository, ISectionRepository, ILearnerRepository, IEnrolmentRepository
{
    public List<Programme> Programmes { get; } = [];
    public List<Course> Courses { get; } = [];
    public List<CourseSection> Sections { get; } = [];
    public List<Learner> Learners { get; } = [];
    public List<Enrolment> Enrolments { get; } = [];

    private static Task<IReadOnlyDictionary<Guid, T>> Many<T>(IEnumerable<T> items, IEnumerable<Guid> ids, Func<T, Guid> key)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyDictionary<Guid, T>>(items.Where(i => set.Contains(key(i))).ToDictionary(key));
    }

    Task<Programme?> IProgrammeRepository.FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Programmes.SingleOrDefault(p => p.Id == id));
    Task<IReadOnlyDictionary<Guid, Programme>> IProgrammeRepository.FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct) => Many(Programmes, ids, p => p.Id);
    Task<bool> IProgrammeRepository.CodeExistsAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    Task<int> IProgrammeRepository.CountCoursesAsync(Guid programmeId, CancellationToken ct) => throw new NotSupportedException();
    Task<PagedResponse<Programme>> IProgrammeRepository.ListAsync(ProgrammeQuery query, CancellationToken ct) => throw new NotSupportedException();
    void IProgrammeRepository.Add(Programme programme) => Programmes.Add(programme);

    Task<Course?> ICourseRepository.FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Courses.SingleOrDefault(c => c.Id == id));
    Task<IReadOnlyDictionary<Guid, Course>> ICourseRepository.FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct) => Many(Courses, ids, c => c.Id);
    Task<bool> ICourseRepository.CodeExistsAsync(string code, CancellationToken ct) => throw new NotSupportedException();
    Task<int> ICourseRepository.CountSectionsAsync(Guid courseId, CancellationToken ct) => throw new NotSupportedException();
    Task<PagedResponse<Course>> ICourseRepository.ListAsync(CourseQuery query, CancellationToken ct) => throw new NotSupportedException();
    void ICourseRepository.Add(Course course) => Courses.Add(course);

    Task<CourseSection?> ISectionRepository.FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Sections.SingleOrDefault(s => s.Id == id));
    Task<IReadOnlyDictionary<Guid, CourseSection>> ISectionRepository.FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct) => Many(Sections, ids, s => s.Id);
    Task<bool> ISectionRepository.CodeExistsAsync(Guid courseId, string code, CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlyList<Session>> ISectionRepository.GetInstructorSessionsElsewhereAsync(Guid instructorUserId, Guid excludingSectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<PagedResponse<CourseSection>> ISectionRepository.ListAsync(SectionQuery query, CancellationToken ct) => throw new NotSupportedException();
    void ISectionRepository.Add(CourseSection section) => Sections.Add(section);

    Task<Learner?> ILearnerRepository.FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Learners.SingleOrDefault(l => l.Id == id));
    Task<Learner?> ILearnerRepository.FindByUserIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(Learners.SingleOrDefault(l => l.UserId == userId));
    Task<IReadOnlyDictionary<Guid, Learner>> ILearnerRepository.FindManyAsync(IEnumerable<Guid> ids, CancellationToken ct) => Many(Learners, ids, l => l.Id);
    Task<bool> ILearnerRepository.LearnerNumberExistsAsync(string learnerNumber, CancellationToken ct) => throw new NotSupportedException();
    Task<bool> ILearnerRepository.UserIdExistsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlySet<Guid>> ILearnerRepository.GetLinkedUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken ct) => throw new NotSupportedException();
    Task<PagedResponse<Learner>> ILearnerRepository.ListAsync(LearnerQuery query, CancellationToken ct) => throw new NotSupportedException();
    void ILearnerRepository.Add(Learner learner) => Learners.Add(learner);

    Task<Enrolment?> IEnrolmentRepository.FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Enrolments.SingleOrDefault(e => e.Id == id));
    Task<int> IEnrolmentRepository.CountActiveInSectionAsync(Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlyDictionary<Guid, int>> IEnrolmentRepository.CountActiveInSectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken ct) => throw new NotSupportedException();
    Task<int> IEnrolmentRepository.CountInSectionAsync(Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<Enrolment?> IEnrolmentRepository.FindByLearnerAndSectionAsync(Guid learnerId, Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<PagedResponse<Enrolment>> IEnrolmentRepository.ListAsync(EnrolmentQuery query, CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlyList<Enrolment>> IEnrolmentRepository.ListActiveInSectionAsync(Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlyList<Enrolment>> IEnrolmentRepository.ListGradableInSectionAsync(Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    Task<Enrolment?> IEnrolmentRepository.FindByUserAndSectionAsync(Guid userId, Guid sectionId, CancellationToken ct) => throw new NotSupportedException();
    void IEnrolmentRepository.Add(Enrolment enrolment) => Enrolments.Add(enrolment);
}

internal sealed class FakeCertificateRepository : ICertificateRepository, ISisUnitOfWork
{
    public List<Certificate> Items { get; } = [];

    /// <summary>Set to make the next commit fail, to prove the stored document is cleaned up.</summary>
    public Exception? FailNextSave { get; set; }

    public int SaveCount { get; private set; }

    public Task<Certificate?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(c => c.Id == id));

    public Task<Certificate?> FindByEnrolmentAsync(Guid enrolmentId, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(c => c.EnrolmentId == enrolmentId));

    public Task<Certificate?> FindByVerificationCodeAsync(string code, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(c => c.VerificationCode == code));

    public Task<IReadOnlyList<Certificate>> ListByLearnerAsync(Guid learnerId, CancellationToken ct) => throw new NotSupportedException("Listing needs the join; covered by the integration test.");

    public void Add(Certificate certificate) => Items.Add(certificate);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        if (FailNextSave is { } failure)
        {
            // A failed commit leaves nothing behind, as the database would.
            FailNextSave = null;
            Items.RemoveAll(c => !_committed.Contains(c.Id));
            throw failure;
        }

        SaveCount++;
        _committed.UnionWith(Items.Select(c => c.Id));
        return Task.CompletedTask;
    }

    private readonly HashSet<Guid> _committed = [];
}

internal sealed class FakeCertificateStore : ICertificateStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public List<string> Deleted { get; } = [];

    public Task<string> SaveAsync(string relativeDirectory, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var path = $"{relativeDirectory}/{Files.Count + 1}.pdf";
        Files[path] = content.ToArray();
        return Task.FromResult(path);
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct) =>
        Task.FromResult<Stream?>(Files.TryGetValue(relativePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string relativePath, CancellationToken ct)
    {
        Files.Remove(relativePath);
        Deleted.Add(relativePath);
        return Task.CompletedTask;
    }
}

internal sealed class FakeRenderer : ICertificateDocumentRenderer
{
    public List<CertificateDocument> Rendered { get; } = [];

    public byte[] Render(CertificateDocument document)
    {
        Rendered.Add(document);
        return "%PDF-fake"u8.ToArray();
    }
}

internal sealed class FakeCodes : IVerificationCodeGenerator
{
    public Queue<string> Next_ { get; } = new();

    public string Next() => Next_.Count > 0 ? Next_.Dequeue() : $"CODE-{Guid.NewGuid():N}"[..23].ToUpperInvariant();
}

/// <summary>Records what the SIS asked of EXT-01/EXT-02/EXT-05; outcome is configurable to prove a failed dispatch never undoes the business change.</summary>
internal sealed class FakeExternal : OpenCampus.Sis.Application.External.IEmailDispatcher, OpenCampus.Sis.Application.External.ISmsDispatcher, OpenCampus.Sis.Application.External.IRecordsArchive
{
    public List<OpenCampus.Sis.Application.External.EmailMessage> Emails { get; } = [];

    public List<OpenCampus.Sis.Application.External.SmsMessage> Sms { get; } = [];

    public List<OpenCampus.Sis.Application.External.ArchiveRecord> Archived { get; } = [];

    public OpenCampus.Sis.Application.External.ExternalResult NextResult { get; set; } = OpenCampus.Sis.Application.External.ExternalResult.Success("ref");

    public Task<OpenCampus.Sis.Application.External.ExternalResult> SendAsync(OpenCampus.Sis.Application.External.EmailMessage message, CancellationToken ct)
    {
        Emails.Add(message);
        return Task.FromResult(NextResult);
    }

    public Task<OpenCampus.Sis.Application.External.ExternalResult> SendAsync(OpenCampus.Sis.Application.External.SmsMessage message, CancellationToken ct)
    {
        Sms.Add(message);
        return Task.FromResult(NextResult);
    }

    public Task<OpenCampus.Sis.Application.External.ExternalResult> ArchiveAsync(OpenCampus.Sis.Application.External.ArchiveRecord record, CancellationToken ct)
    {
        Archived.Add(record);
        return Task.FromResult(NextResult);
    }
}
