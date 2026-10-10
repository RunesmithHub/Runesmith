using System.Text.Json;
using Runesmith.Shell.Web;

namespace Runesmith.Shell.Tests.Web;

public sealed class WebMessagesTests
{
    [Theory]
    [InlineData("""{"kind":"refresh","count":3}""")]
    [InlineData("""["a",1,null]""")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void AcceptsAnyJsonValue(string json) => Assert.True(WebMessages.IsJson(json));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{kind: 'refresh'}")]
    [InlineData("alert(1)")]
    [InlineData("{} {}")]
    [InlineData(WebMessages.Ready)]
    [InlineData(null)]
    public void RejectsWhatIsNotOneJsonValue(string? text) => Assert.False(WebMessages.IsJson(text));

    [Fact]
    public void DeliversAMessageAsAStringLiteralThePageParses()
    {
        const string json = """{"text":"</script><script>alert('x')</script>","line":"a\u2028b","quote":"\"; alert(1); \""}""";

        var script = WebMessages.DeliveryScript(json);

        const string prefix = "globalThis.__runesmithDeliver && globalThis.__runesmithDeliver(";
        Assert.StartsWith(prefix, script, StringComparison.Ordinal);
        Assert.EndsWith(");", script, StringComparison.Ordinal);
        var literal = script[prefix.Length..^2];
        Assert.DoesNotContain("<", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("'", literal, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", literal, StringComparison.Ordinal);
        Assert.Equal(json, JsonSerializer.Deserialize<string>(literal));
    }

    [Fact]
    public void TheBridgeSendsJsonAndAReadyMarkerPagesCannotForge()
    {
        Assert.Contains("JSON.stringify", WebMessages.BridgeScript, StringComparison.Ordinal);
        Assert.Contains("'\\u0001ready'", WebMessages.BridgeScript, StringComparison.Ordinal);
        Assert.False(WebMessages.IsJson(WebMessages.Ready));
    }
}
