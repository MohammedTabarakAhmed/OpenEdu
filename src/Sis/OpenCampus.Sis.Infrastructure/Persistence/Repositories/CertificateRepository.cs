using Microsoft.EntityFrameworkCore;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.Sis.Infrastructure.Persistence.Repositories;

internal sealed class CertificateRepository(SisDbContext db) : ICertificateRepository
{
    public Task<Certificate?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Certificates.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Certificate?> FindByEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken) =>
        db.Certificates.SingleOrDefaultAsync(c => c.EnrolmentId == enrolmentId, cancellationToken);

    public Task<Certificate?> FindByVerificationCodeAsync(string verificationCode, CancellationToken cancellationToken) =>
        db.Certificates.SingleOrDefaultAsync(c => c.VerificationCode == verificationCode, cancellationToken);

    public async Task<IReadOnlyList<Certificate>> ListByLearnerAsync(Guid learnerId, CancellationToken cancellationToken) =>
        await db.Certificates
            .Join(db.Enrolments.Where(e => e.LearnerId == learnerId), c => c.EnrolmentId, e => e.Id, (c, _) => c)
            .OrderByDescending(c => c.IssuedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(Certificate certificate) => db.Certificates.Add(certificate);
}
