using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.SharedKernel;

public class SequentialGuidTests
{
    [Fact]
    public void NewGuid_IsNeverEmpty()
    {
        SequentialGuid.NewGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void NewGuid_IsUnique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => SequentialGuid.NewGuid()).ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void NewGuid_IsAscendingInSqlServerOrder()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => SequentialGuid.NewGuid()).ToList();

        var sorted = ids.OrderBy(id => id, SqlServerGuidComparer.Instance).ToList();

        sorted.ShouldBe(ids);
    }

    // Mirrors SQL Server's uniqueidentifier comparison: bytes 10-15, then 8-9, 7, 6, 5-4, 3-0.
    private sealed class SqlServerGuidComparer : IComparer<Guid>
    {
        public static readonly SqlServerGuidComparer Instance = new();

        private static readonly int[] ByteOrder = [10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0];

        public int Compare(Guid x, Guid y)
        {
            var xb = x.ToByteArray();
            var yb = y.ToByteArray();

            foreach (var i in ByteOrder)
            {
                var c = xb[i].CompareTo(yb[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        }
    }
}
