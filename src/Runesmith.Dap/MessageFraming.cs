using System.Globalization;
using System.Text;

namespace Runesmith.Dap;

/// <summary>Reads messages framed by <c>Content-Length</c> headers from a stream, however the stream splits or joins them.</summary>
internal sealed class MessageReader(Stream stream)
{
    private const string ContentLength = "Content-Length:";

    /// <summary>The largest message body accepted; a larger length means the stream is not speaking the protocol.</summary>
    public const int MaximumLength = 256 * 1024 * 1024;

    private byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    /// <summary>Reads the next message body, or returns null at the end of the stream.</summary>
    /// <exception cref="FormatException">A header is not a valid <c>Content-Length</c>.</exception>
    public async ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken)
    {
        var length = -1;
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;
            if (line.Length == 0)
            {
                if (length >= 0)
                    break;
                continue;
            }

            if (line.StartsWith(ContentLength, StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line.AsSpan(ContentLength.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length) || length > MaximumLength)
                    throw new FormatException($"{line} is not a length up to {MaximumLength} bytes.");
            }
        }

        var body = new byte[length];
        var copied = Math.Min(length, _end - _start);
        _buffer.AsSpan(_start, copied).CopyTo(body);
        _start += copied;
        while (copied < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(copied), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;
            copied += read;
        }

        return body;
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var index = _buffer.AsSpan(_start, _end - _start).IndexOf("\r\n"u8);
            if (index >= 0)
            {
                var line = Encoding.ASCII.GetString(_buffer, _start, index);
                _start += index + 2;
                return line;
            }

            if (_end - _start > 8 * 1024)
                throw new FormatException("A header line is longer than 8 KB.");
            if (!await FillAsync(cancellationToken).ConfigureAwait(false))
                return null;
        }
    }

    private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
            Array.Resize(ref _buffer, _buffer.Length * 2);
        var read = await stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
        _end += read;
        return read > 0;
    }
}

/// <summary>Writes messages framed by <c>Content-Length</c> headers, one whole message at a time.</summary>
internal sealed class MessageWriter(Stream stream) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task WriteAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n"));
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
