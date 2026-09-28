using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCampus.Sis.Application.Abstractions;

namespace OpenCampus.Sis.Infrastructure.Persistence;

/// <summary>
/// Learner numbers from <c>sis.LearnerNumberSequence</c> (migration <c>LearnerNumberSequence</c>, starting at 1000),
/// formatted <c>L{year}{value:0000}</c>. The demonstration data occupies L2026001–L2026040, so the first generated
/// number, L20261000, can never collide with it. A sequence value is consumed even if the surrounding transaction
/// rolls back — gaps are acceptable; duplicates are not.
/// </summary>
internal sealed class SequenceLearnerNumberGenerator(SisDbContext db, TimeProvider clock) : ILearnerNumberGenerator
{
    public async Task<string> NextAsync(CancellationToken cancellationToken)
    {
        // Issued through a plain command: SQL Server refuses NEXT VALUE FOR inside the subquery EF wraps around SqlQueryRaw.
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR [sis].[LearnerNumberSequence]";
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var value = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            return $"L{clock.GetUtcNow().Year}{value:0000}";
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
