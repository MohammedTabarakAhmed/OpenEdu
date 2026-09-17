namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>
/// SDD 18.4: in-process cache for low-change reference data with bounded expiry and explicit invalidation after
/// amendment. Programmes are the first cacheable read path — their names decorate course listings, the learner
/// catalogue and certificate responses on every request, yet change only through administrative amendment.
/// Nothing user-specific or authorization-relevant is ever cached (only the programme rows themselves).
/// </summary>
public interface IReferenceDataCache
{
    /// <summary>Drops the cached programme after an amendment or deletion so the next read reflects the database.</summary>
    void InvalidateProgramme(Guid programmeId);
}
