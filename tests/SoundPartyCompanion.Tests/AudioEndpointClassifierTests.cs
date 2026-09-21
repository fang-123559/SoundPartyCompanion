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
}
