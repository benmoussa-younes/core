using System.Buffers;
using System.Text;

namespace Core.Platform.Linux;

internal static class LinuxPathEncoding
{
    internal static byte[] Rent(string path, out int length)
    {
        if (path.Contains('\0'))
        {
            throw new ArgumentException("A path cannot contain a NUL character.", nameof(path));
        }

        var byteCount = Encoding.UTF8.GetByteCount(path);

        // WHY: a path of 2^31-1 UTF-8 bytes is the only input that overflows the +1.
        if (byteCount == int.MaxValue)
        {
            throw new ArgumentException("The path is too long to encode.", nameof(path));
        }

        var rented = ArrayPool<byte>.Shared.Rent(byteCount + 1);
        var written = Encoding.UTF8.GetBytes(path, rented);
        rented[written] = 0;
        length = written + 1;

        return rented;
    }
}
