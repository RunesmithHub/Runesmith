using System.Globalization;
using System.Text;

namespace Runesmith.Lsp;

/// <summary>Reads messages framed by <c>Content-Length</c> headers from a stream, however the stream splits or joins them.</summary>
internal sealed class MessageReader(Stream stream)
{
    private const string ContentLength = "Content-Length:";
    private byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    /// <summary>Reads the next message body, or returns null at the end of the stream.</summary>
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
                length = int.Parse(line.AsSpan(ContentLength.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
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
