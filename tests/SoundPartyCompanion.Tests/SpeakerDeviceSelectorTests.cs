using SoundPartyCompanion.Core.Devices;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class SpeakerDeviceSelectorTests
{
    [Fact]
    public void SelectPrefersSavedDeviceKey()
    {
        var discovery = CreateDiscovery(
            Device("bt-11111111111111111111111111111111", "Another speaker", connected: false),
            Device("bt-22222222222222222222222222222222", "Xiaomi Sound Party", connected: true));
        var saved = new SpeakerSelection(
            "bt-11111111111111111111111111111111",
            "Old display name");

        var result = SpeakerDeviceSelector.Select(
            discovery,
            saved,
            ["Xiaomi Sound Party"]);

        Assert.NotNull(result);
        Assert.Equal("bt-11111111111111111111111111111111", result.DeviceKey);
        Assert.Equal("Another speaker", result.DisplayName);
    }

    [Fact]
    public void SelectMatchesPreferredNameIgnoringCase()
    {
        var discovery = CreateDiscovery(
            Device("bt-11111111111111111111111111111111", "XIAOMI SOUND PARTY", connected: true));

        var result = SpeakerDeviceSelector.Select(
            discovery,
            savedSelection: null,
            ["Xiaomi Sound Party"]);

        Assert.NotNull(result);
        Assert.Equal("bt-11111111111111111111111111111111", result.DeviceKey);
    }

    [Fact]
    public void SelectUsesOnlyConnectedDeviceWhenNamesAreDuplicated()
    {
        var discovery = CreateDiscovery(
            Device("bt-11111111111111111111111111111111", "Xiaomi Sound Party", connected: false),
            Device("bt-22222222222222222222222222222222", "Xiaomi Sound Party", connected: true));

        var result = SpeakerDeviceSelector.Select(
            discovery,
            savedSelection: null,
            ["Xiaomi Sound Party"]);

        Assert.NotNull(result);
        Assert.Equal("bt-22222222222222222222222222222222", result.DeviceKey);
    }

    [Fact]
    public void SelectReturnsNullForAmbiguousOfflineMatches()
    {
        var discovery = CreateDiscovery(
            Device("bt-11111111111111111111111111111111", "Xiaomi Sound Party", connected: false),
            Device("bt-22222222222222222222222222222222", "Xiaomi Sound Party", connected: false));

        var result = SpeakerDeviceSelector.Select(
            discovery,
            savedSelection: null,
            ["Xiaomi Sound Party"]);

        Assert.Null(result);
    }

    private static SpeakerDiscoverySnapshot CreateDiscovery(params SpeakerDevice[] devices) =>
        new(true, devices, DateTimeOffset.UtcNow);

    private static SpeakerDevice Device(string key, string name, bool connected) =>
        new(key, name, IsPaired: true, connected, DateTimeOffset.UtcNow);
}
