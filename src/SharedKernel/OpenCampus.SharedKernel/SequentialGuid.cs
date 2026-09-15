using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenCampus.SharedKernel;

public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    // SQL Server orders uniqueidentifier by bytes 10-15 first, then 8-9, so the
    // monotonic counter is placed there and the leading bytes stay random.
    public static Guid NewGuid()
    {
        Span<byte> guid = stackalloc byte[16];
        Span<byte> counter = stackalloc byte[sizeof(long)];

        RandomNumberGenerator.Fill(guid[..8]);
        BinaryPrimitives.WriteInt64BigEndian(counter, Interlocked.Increment(ref _counter));

        guid[8] = counter[6];
        guid[9] = counter[7];
        counter[..6].CopyTo(guid[10..]);

        return new Guid(guid);
    }
}
