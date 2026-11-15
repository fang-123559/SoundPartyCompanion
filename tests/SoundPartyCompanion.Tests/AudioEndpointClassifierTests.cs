using SoundPartyCompanion.Core.Audio;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class AudioEndpointClassifierTests
{
    [Theory]
    [InlineData("Xiaomi蓝牙音箱Pro", "Speakers (Xiaomi蓝牙音箱Pro)")]
    [InlineData("Xiaomi Sound Party", "XIAOMI SOUND PARTY - Stereo")]
    [InlineData("Xiaomi 蓝牙音箱 Pro", "扬声器 (Xiaomi蓝牙音箱Pro)")]
    public void NamesMatchIgnoresSpacingPunctuationAndCase(
        string deviceName,
        string endpointName)
    {
        Assert.True(AudioEndpointClassifier.NamesMatch(deviceName, endpointName));
    }

    [Theory]
    [InlineData("Headset (Xiaomi Sound Party Hands-Free AG Audio)")]
    [InlineData("耳麦 (Xiaomi蓝牙音箱Pro 免提)")]
    public void RenderHandsFreeEndpointsAreNotClassifiedAsA2dp(string endpointName)
    {
        var result = AudioEndpointClassifier.Classify(
            endpointName,
            AudioEndpointDirection.Render);

        Assert.Equal(SpeakerAudioEndpointKind.HandsFreeRender, result);
    }

    [Fact]
    public void StereoRenderEndpointIsClassifiedAsA2dp()
    {
        var result = AudioEndpointClassifier.Classify(
            "Speakers (Xiaomi Sound Party Stereo)",
            AudioEndpointDirection.Render);

        Assert.Equal(SpeakerAudioEndpointKind.A2dpRender, result);
    }

    [Fact]
    public void MatchingCaptureEndpointIsClassifiedAsHandsFree()
    {
        var result = AudioEndpointClassifier.Classify(
            "Microphone (Xiaomi Sound Party)",
            AudioEndpointDirection.Capture);

        Assert.Equal(SpeakerAudioEndpointKind.HandsFreeCapture, result);
    }

    [Theory]
    [InlineData("Xiaomi 蓝牙音箱 Pro", "Speakers (Xiaomi Sound Party)")]
    [InlineData("Xiaomi Sound Party", "扬声器 (Xiaomi蓝牙音箱Pro)")]
    public void SelectsTheOnlyActiveKnownAlias(string deviceName, string endpointName)
    {
        var endpoint = PlaybackEndpoint("alias", endpointName);
        var other = PlaybackEndpoint("other", "Speakers (Another Speaker)") with { IsDefault = true };
        var matches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, [endpoint, other]);
        Assert.Equal(endpoint, AudioEndpointSelection.SelectA2dpEndpoint(matches));
    }

    [Fact]
    public void AliasDoesNotOverrideAnExactOrAmbiguousMatch()
    {
        const string deviceName = "Xiaomi蓝牙音箱Pro";
        var exact = PlaybackEndpoint("exact", "扬声器 (Xiaomi蓝牙音箱Pro)");
        var alias = PlaybackEndpoint("alias", "Speakers (Xiaomi Sound Party)") with { IsDefault = true };

        var exactMatches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, [exact, alias]);
        Assert.Equal(exact, AudioEndpointSelection.SelectA2dpEndpoint(exactMatches));

        var unpluggedMatches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, [exact with { IsActive = false }, alias]);
        Assert.Null(AudioEndpointSelection.SelectA2dpEndpoint(unpluggedMatches));

        var ambiguousMatches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, [alias, alias with { EndpointId = "second" }]);
        Assert.Null(AudioEndpointSelection.SelectA2dpEndpoint(ambiguousMatches));

        var handsFree = alias with { Kind = SpeakerAudioEndpointKind.HandsFreeRender };
        var handsFreeMatches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, [handsFree]);
        Assert.Null(AudioEndpointSelection.SelectA2dpEndpoint(handsFreeMatches));
    }

    private static AudioEndpointDescriptor PlaybackEndpoint(string id, string name) =>
        new(id, name, AudioEndpointDirection.Render, SpeakerAudioEndpointKind.A2dpRender, IsActive: true, IsDefault: false);
}
