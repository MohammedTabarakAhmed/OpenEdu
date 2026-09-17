using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Infrastructure.Caching;

/// <summary>Appendix B-style bound for 18.4: how long a cached programme may be served before it is re-read.</summary>
public sealed class ReferenceDataCacheOptions
{
    public const string SectionName = "ReferenceDataCache";

    public TimeSpan ProgrammeExpiry { get; set; } = TimeSpan.FromMinutes(5);

    public bool Validate() => ProgrammeExpiry > TimeSpan.Zero && ProgrammeExpiry <= TimeSpan.FromHours(24);
}

/// <summary>
/// 18.4 decorator over the programme repository. Only the batched, read-only enrichment lookup (<see cref="FindManyAsync"/>)
/// is served from the in-process cache; the single-entity read used by amendment paths always goes to the database so a
/// tracked entity is what gets modified. Cached rows are detached instances from a disposed context — consumers of
/// <see cref="FindManyAsync"/> only read them. Entries expire after the configured bound and are dropped explicitly
/// through <see cref="IReferenceDataCache"/> when a programme is amended or deleted.
/// </summary>
internal sealed class CachedProgrammeRepository(IProgrammeRepository inner, IMemoryCache cache, IOptions<ReferenceDataCacheOptions> options)
    : IProgrammeRepository, IReferenceDataCache
{
    private static string Key(Guid id) => $"sis:programme:{id}";

    public Task<Programme?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => inner.FindByIdAsync(id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Programme>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToArray();
        var result = new Dictionary<Guid, Programme>(wanted.Length);
        var missing = new List<Guid>();
        foreach (var id in wanted)
        {
            if (cache.TryGetValue(Key(id), out Programme? hit) && hit is not null)
            {
                result[id] = hit;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            // One round trip for every miss (NFR-04), then each row is cached individually with the bounded expiry.
            var fetched = await inner.FindManyAsync(missing, cancellationToken);
            foreach (var (id, programme) in fetched)
            {
                cache.Set(Key(id), programme, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Value.ProgrammeExpiry });
                result[id] = programme;
            }
        }

        return result;
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) => inner.CodeExistsAsync(code, cancellationToken);

    public Task<int> CountCoursesAsync(Guid programmeId, CancellationToken cancellationToken) => inner.CountCoursesAsync(programmeId, cancellationToken);

    public Task<PagedResponse<Programme>> ListAsync(ProgrammeQuery query, CancellationToken cancellationToken) => inner.ListAsync(query, cancellationToken);

    public void Add(Programme programme) => inner.Add(programme);

    public void InvalidateProgramme(Guid programmeId) => cache.Remove(Key(programmeId));
}
