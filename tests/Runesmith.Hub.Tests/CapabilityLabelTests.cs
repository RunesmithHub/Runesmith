using RunesmithHub.Protocol;

namespace Runesmith.Hub.Tests;

public sealed class CapabilityLabelTests
{
    [Fact]
    public void EveryCapabilityTheProtocolKnowsHasAMessageOfRunesmith()
    {
        foreach (var capability in Capabilities.All)
            Assert.False(string.IsNullOrWhiteSpace(HubClient.KnownCapabilityLabel(capability)), capability);
    }

    [Fact]
    public void TheEnglishMessagesSayWhatTheProtocolsDefaultsSay()
    {
        foreach (var (capability, label) in Capabilities.DefaultLabels)
            Assert.Equal(label, HubClient.KnownCapabilityLabel(capability));
    }

    [Fact]
    public void ACapabilityRunesmithDoesNotKnowHasNoMessage() =>
        Assert.Null(HubClient.KnownCapabilityLabel("telepathy"));
}
