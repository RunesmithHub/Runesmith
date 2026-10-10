using System.Text;

namespace Runesmith.Dap.Tests;

public sealed class MessageFramingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsMessagesSplitAcrossReadsAndJoinedInOneRead()
    {
        var bytes = Concat(Frame("""{"a":1}"""), Frame("""{"b":"é"}"""), Frame("{}"));
        var reader = new MessageReader(new TrickleStream(bytes, 3));

        Assert.Equal("""{"a":1}""", Encoding.UTF8.GetString((await reader.ReadAsync(Token))!));
        Assert.Equal("""{"b":"é"}""", Encoding.UTF8.GetString((await reader.ReadAsync(Token))!));
        Assert.Equal("{}", Encoding.UTF8.GetString((await reader.ReadAsync(Token))!));
        Assert.Null(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task IgnoresOtherHeadersAndTheCaseOfContentLength()
    {
        var body = """{"seq":1}""";
        var bytes = Encoding.ASCII.GetBytes($"content-length: {body.Length}\r\nContent-Type: application/json\r\n\r\n{body}");
        var reader = new MessageReader(new MemoryStream(bytes));

        Assert.Equal(body, Encoding.UTF8.GetString((await reader.ReadAsync(Token))!));
    }

    [Fact]
    public async Task AStreamThatEndsInsideABodyReadsAsTheEnd()
    {
        var bytes = Frame("""{"seq":1,"type":"event"}""");
        var reader = new MessageReader(new MemoryStream(bytes[..^4]));

        Assert.Null(await reader.ReadAsync(Token));
    }

    [Fact]
    public async Task RejectsALengthThatIsNotANumberOrTooLarge()
    {
        await Assert.ThrowsAsync<FormatException>(async () => await new MessageReader(new MemoryStream("Content-Length: x\r\n\r\n"u8.ToArray())).ReadAsync(Token));
        await Assert.ThrowsAsync<FormatException>(async () => await new MessageReader(new MemoryStream("Content-Length: 999999999999\r\n\r\n"u8.ToArray())).ReadAsync(Token));
    }

    [Fact]
    public async Task WritesTheLengthInBytesBeforeTheBody()
    {
        using var stream = new MemoryStream();
        using var writer = new MessageWriter(stream);

        await writer.WriteAsync(Encoding.UTF8.GetBytes("""{"v":"ü"}"""), Token);

        Assert.Equal("Content-Length: 10\r\n\r\n{\"v\":\"ü\"}", Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static byte[] Frame(string json) => Runesmith.Tests.Dap.FakeDebugAdapter.Frame(json);

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    // Hands out at most a few bytes per read, as a pipe under load can.
    private sealed class TrickleStream(byte[] bytes, int step) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(step, buffer.Length)], cancellationToken);
    }
}
