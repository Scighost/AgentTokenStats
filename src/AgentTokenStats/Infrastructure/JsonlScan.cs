using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentTokenStats.Infrastructure;

public interface IUtf8LineParser<T>
{
    T? Parse(ReadOnlySpan<byte> utf8, out bool invalid, out bool skipped);
}

public readonly record struct Utf8LineResult<T>(int Line, T? Value, bool Invalid, bool Skipped);

public static class JsonlScan
{
    private const int BufferSize = 64 * 1024;
    private const int MaxLineBytes = 4 * 1024 * 1024;

    public static IEnumerable<string> EnumerateFiles(string root, string searchPattern)
    {
        if (!Directory.Exists(root))
            yield break;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> subs = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(dir, searchPattern);
            }
            catch
            {
                /* skip unreadable directories */
            }

            try
            {
                subs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                /* skip */
            }

            foreach (var file in files)
                yield return file;
            foreach (var sub in subs)
                pending.Push(sub);
        }
    }

    public static async IAsyncEnumerable<Utf8LineResult<T>> ParseLinesAsync<T>(
        string path,
        IUtf8LineParser<T> parser,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);
        }
        catch
        {
            yield break;
        }

        var buffer = new byte[BufferSize];
        var lineNo = 0;
        var buffered = 0;
        var skipBom = true;
        await using (stream)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(buffered), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    yield break;
                }

                var end = buffered + read;
                if (skipBom && end >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                {
                    Buffer.BlockCopy(buffer, 3, buffer, 0, end - 3);
                    end -= 3;
                    skipBom = false;
                }
                else if (end > 0)
                {
                    skipBom = false;
                }

                var consumed = 0;
                for (var i = 0; i < end; i++)
                {
                    if (buffer[i] != (byte)'\n')
                        continue;
                    lineNo++;
                    var slice = buffer.AsSpan(consumed, i - consumed);
                    if (slice.Length > 0 && slice[^1] == (byte)'\r')
                        slice = slice[..^1];
                    consumed = i + 1;
                    var parsed = ParseSlice(parser, lineNo, slice);
                    if (parsed is { } row)
                        yield return row;
                }

                if (read == 0)
                {
                    if (consumed < end)
                    {
                        lineNo++;
                        var parsed = ParseSlice(parser, lineNo, buffer.AsSpan(consumed, end - consumed));
                        if (parsed is { } row)
                            yield return row;
                    }

                    yield break;
                }

                var remain = end - consumed;
                if (remain == 0)
                {
                    buffered = 0;
                    continue;
                }

                if (remain >= buffer.Length)
                {
                    if (buffer.Length >= MaxLineBytes)
                    {
                        lineNo++;
                        yield return new Utf8LineResult<T>(lineNo, default, true, false);
                        buffered = 0;
                        continue;
                    }

                    var grown = new byte[Math.Min(MaxLineBytes, buffer.Length * 2)];
                    Buffer.BlockCopy(buffer, consumed, grown, 0, remain);
                    buffer = grown;
                    buffered = remain;
                    continue;
                }

                Buffer.BlockCopy(buffer, consumed, buffer, 0, remain);
                buffered = remain;
            }
        }
    }

    private static Utf8LineResult<T>? ParseSlice<T>(
        IUtf8LineParser<T> parser,
        int line,
        ReadOnlySpan<byte> utf8)
    {
        utf8 = TrimAscii(utf8);
        if (utf8.IsEmpty)
            return null;
        try
        {
            var value = parser.Parse(utf8, out var invalid, out var skipped);
            return new Utf8LineResult<T>(line, value, invalid, skipped);
        }
        catch (JsonException)
        {
            return new Utf8LineResult<T>(line, default, true, false);
        }
    }

    private static ReadOnlySpan<byte> TrimAscii(ReadOnlySpan<byte> span)
    {
        var start = 0;
        var end = span.Length;
        while (start < end && span[start] is 0x20 or 0x09 or 0x0D or 0x0A)
            start++;
        while (end > start && span[end - 1] is 0x20 or 0x09 or 0x0D or 0x0A)
            end--;
        return span[start..end];
    }
}
