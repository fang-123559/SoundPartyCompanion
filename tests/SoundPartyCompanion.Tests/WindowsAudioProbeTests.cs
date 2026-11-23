using System.Runtime.InteropServices;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Windows.Audio;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class WindowsAudioProbeTests
{
    private static readonly Guid SpeakerContainer = Guid.NewGuid();
    private static SpeakerDevice Speaker => new("synthetic-speaker", "Xiaomi蓝牙音箱Pro", true, true, DateTimeOffset.UtcNow)
        { ContainerId = SpeakerContainer };

    [Theory]
    [InlineData("客厅扬声器", true)]
    [InlineData("Headset (Xiaomi Sound Party)", true)]
    [InlineData("Headset (Xiaomi Sound Party Hands-Free AG Audio)", false)]
    public async Task DefaultProbeChecksOnlyDefaultMetadataWithoutActivatingVolume(string name, bool expected)
    {
        var endpoint = new FakeEndpoint("default", name, SpeakerContainer);
        var enumerator = new FakeEnumerator(endpoint, [endpoint]);
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => enumerator, logger);

        Assert.Equal(expected, await new WindowsDefaultOutputProbe(controller).IsCurrentOutputAsync(Speaker));
        Assert.Equal(0, enumerator.EnumerationCount);
        Assert.Equal(0, endpoint.Activations);
        Assert.Equal(1, endpoint.PropertyOpens);
        var message = Assert.Single(logger.Messages, item => item.StartsWith("Audio probe completed."));
        Assert.Contains("Operation=DefaultOutput", message);
        Assert.Contains("ControlsMs=0 IdentityReads=1", message);
        Assert.Contains(message, LogPrivacyFilter.FilterForExport($"{DateTimeOffset.UtcNow:O} [Information] {message}"));
    }

    [Fact]
    public async Task ForeignDefaultContainerSkipsNameAndNeverScansOtherEndpoints()
    {
        var endpoint = new FakeEndpoint("foreign", "Xiaomi蓝牙音箱Pro", Guid.NewGuid());
        var enumerator = new FakeEnumerator(endpoint, [endpoint]);
        var controller = new WindowsAudioEndpointController(() => enumerator);

        Assert.False(await controller.IsCurrentOutputAsync(Speaker));
        Assert.Equal(0, enumerator.EnumerationCount);
        Assert.Equal(0, endpoint.NameReads);
        Assert.Equal(0, endpoint.Activations);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task MissingContainerPreservesUniqueAliasFallbackWithoutVolume(int aliasCount, bool expected)
    {
        var endpoints = Enumerable.Range(0, aliasCount)
            .Select(index => new FakeEndpoint($"alias-{index}", "Speakers (Xiaomi Sound Party)", null)).ToArray();
        var enumerator = new FakeEnumerator(endpoints[0], endpoints);
        var controller = new WindowsAudioEndpointController(() => enumerator);

        Assert.Equal(expected, await controller.IsCurrentOutputAsync(Speaker));
        Assert.Equal(AudioDeviceState.Active, enumerator.RequestedStates);
        Assert.All(endpoints, endpoint => Assert.Equal(0, endpoint.Activations));
    }

    [Fact]
    public async Task SnapshotSkipsPropertiesIfEndpointDisconnectsDuringActiveEnumeration()
    {
        var disconnected = new FakeEndpoint("racing", "Xiaomi蓝牙音箱Pro", SpeakerContainer)
            { State = AudioDeviceState.Unplugged };
        var foreign = new FakeEndpoint("foreign", "Xiaomi蓝牙音箱Pro", Guid.NewGuid());
        var enumerator = new FakeEnumerator(foreign, [disconnected, foreign]);
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => enumerator, logger);

        var snapshot = await controller.GetSnapshotAsync(Speaker);
        Assert.False(snapshot.HasA2dpEndpoint);
        Assert.Null(snapshot.VolumePercent);
        Assert.Equal(AudioDeviceState.Active, enumerator.RequestedStates);
        Assert.Equal(0, disconnected.PropertyOpens);
        Assert.Equal(0, foreign.NameReads);
        Assert.Equal(0, disconnected.Activations + foreign.Activations);
        var message = Assert.Single(logger.Messages, item => item.StartsWith("Audio probe completed."));
        Assert.Contains("IdentityReads=1 Outcome=NoEndpoint", message);
    }

    [Fact]
    public async Task TimingLogsSuppressRepeatedResultsButRecordRecovery()
    {
        var endpoint = new FakeEndpoint("default", "客厅扬声器", SpeakerContainer);
        var enumerator = new FakeEnumerator(endpoint, [endpoint]);
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => enumerator, logger);
        var offline = Speaker with { IsConnected = false };

        await controller.IsCurrentOutputAsync(offline);
        await controller.IsCurrentOutputAsync(offline);
        Assert.Single(logger.Messages);
        Assert.Equal(0, enumerator.DefaultRequests);
        await controller.IsCurrentOutputAsync(Speaker);
        Assert.Equal(2, logger.Messages.Count);
        Assert.Contains("Outcome=Offline", logger.Messages[0]);
        Assert.Contains("Outcome=Speaker", logger.Messages[1]);
    }

    [Fact]
    public async Task CanceledQueryDoesNotTouchWindows()
    {
        var controller = new WindowsAudioEndpointController(() => throw new InvalidOperationException("Must not create enumerator"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.IsCurrentOutputAsync(Speaker, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.GetSnapshotAsync(Speaker, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DecisionsDistinguishForeignContainerHandsFreeAndMissingNameWithoutExposingIdentity()
    {
        var foreign = new FakeEndpoint("private-foreign-endpoint", "private friendly name", Guid.NewGuid());
        var handsFree = new FakeEndpoint("private-speaker-endpoint", "Xiaomi蓝牙音箱Pro Hands-Free", SpeakerContainer);
        var unnamed = new FakeEndpoint("private-unnamed-endpoint", "", null);
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => new FakeEnumerator(foreign, [foreign, handsFree, unnamed]), logger);
        await controller.GetSnapshotAsync(Speaker);
        Assert.Contains(logger.Messages, message => message.Contains("Reason=ContainerMismatch"));
        Assert.Contains(logger.Messages, message => message.Contains("Reason=HandsFree"));
        Assert.Contains(logger.Messages, message => message.Contains("Reason=NameMissing"));
        Assert.Equal(0, foreign.NameReads);
        Assert.Equal(0, foreign.Activations + handsFree.Activations + unnamed.Activations);
        foreach (var message in logger.Messages)
        {
            Assert.DoesNotContain("private", message);
            Assert.DoesNotContain(SpeakerContainer.ToString(), message);
            Assert.Contains(message, LogPrivacyFilter.FilterForExport($"{DateTimeOffset.UtcNow:O} [Information] {message}"));
        }
    }

    [Fact]
    public void DecisionAliasesPersistAcrossStateAndContainerChangesAndSuppressUnchangedSamples()
    {
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => throw new InvalidOperationException(), logger);
        var foreign = Guid.NewGuid();
        controller.RecordEndpointDecision("private-id", AudioDeviceState.Active, SpeakerContainer, foreign, "ContainerMismatch", false);
        controller.RecordEndpointDecision("private-id", AudioDeviceState.Active, SpeakerContainer, foreign, "ContainerMismatch", false);
        Assert.Single(logger.Messages);
        controller.RecordEndpointDecision("private-id", AudioDeviceState.Active, SpeakerContainer, SpeakerContainer, "Selected", true);
        Assert.Equal(2, logger.Messages.Count);
        Assert.All(logger.Messages, message => Assert.Contains("Endpoint=1", message));
        Assert.Contains("ExpectedContainer=1 ActualContainer=2", logger.Messages[0]);
        Assert.Contains("ExpectedContainer=1 ActualContainer=1", logger.Messages[1]);
    }

    [Fact]
    public void StartupInventoryReadsStatesWithoutPropertiesOrVolume()
    {
        var offline = new FakeEndpoint("private-offline", "private name", SpeakerContainer) { State = AudioDeviceState.Unplugged };
        var active = new FakeEndpoint("private-active", "private other", Guid.NewGuid());
        var removed = new FakeEndpoint("private-removed", "private name", null)
            { StateResult = unchecked((int)0x80070490) };
        var enumerator = new FakeEnumerator(active, [offline, removed, active]);
        var logger = new MemoryLogger();
        var controller = new WindowsAudioEndpointController(() => enumerator, logger);
        controller.LogInitialEndpointStates();
        Assert.Equal(AudioDeviceState.All, enumerator.RequestedStates);
        Assert.Equal(0, offline.PropertyOpens + active.PropertyOpens + offline.Activations + active.Activations);
        Assert.Contains(logger.Messages, item => item.Contains("Kind=InitialRender State=8"));
        Assert.Contains(logger.Messages, item => item.Contains("Kind=InitialRender State=1"));
        Assert.Contains("Audio endpoint inventory item unavailable.", logger.Messages);
        Assert.All(logger.Messages, message => Assert.Contains(message,
            LogPrivacyFilter.FilterForExport($"{DateTimeOffset.UtcNow:O} [Information] {message}")));
    }

    [Fact]
    public async Task NotificationsRecordInactiveAndUnmatchedEndpointsBeforeActivationLookup()
    {
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new WindowsAudioEndpointController.PlaybackEndpointCallback(
            (id, at) => { observed.Enqueue("lookup"); completion.TrySetResult(); },
            (role, id, at) => { },
            (id, kind, state, at) => observed.Enqueue($"{kind}:{state}"));
        callback.OnDeviceStateChanged("private-id", (uint)AudioDeviceState.Active);
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "State:1", "lookup" }, observed.ToArray());
        var inactive = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inactiveCallback = new WindowsAudioEndpointController.PlaybackEndpointCallback(
            (id, at) => throw new InvalidOperationException("Inactive must not run activation lookup"),
            (role, id, at) => { },
            (id, kind, state, at) => inactive.TrySetResult($"{kind}:{state}"));
        inactiveCallback.OnDeviceStateChanged("private-id", (uint)AudioDeviceState.Unplugged);
        Assert.Equal("State:8", await inactive.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class MemoryLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];
        public void Write(AppLogLevel level, string message, Exception? exception = null) => Messages.Add(message);
    }

    private sealed class FakeEnumerator(FakeEndpoint defaultEndpoint, FakeEndpoint[] endpoints) : IMultimediaDeviceEnumerator
    {
        public int EnumerationCount { get; private set; }
        public int DefaultRequests { get; private set; }
        public AudioDeviceState RequestedStates { get; private set; }
        public int EnumAudioEndpoints(AudioDataFlow flow, AudioDeviceState states, out IMultimediaDeviceCollection devices)
        {
            Assert.Equal(AudioDataFlow.Render, flow);
            EnumerationCount++;
            RequestedStates = states;
            // Include an endpoint that raced to Unplugged after the native enumeration.
            devices = new FakeCollection(endpoints);
            return 0;
        }
        public int GetDefaultAudioEndpoint(AudioDataFlow flow, AudioDeviceRole role, out IMultimediaDevice device)
        {
            Assert.Equal(AudioDeviceRole.Multimedia, role);
            DefaultRequests++;
            device = defaultEndpoint;
            return 0;
        }
        public int GetDevice(string id, out IMultimediaDevice device)
        {
            device = endpoints.Single(endpoint => endpoint.Id == id);
            return 0;
        }
        public int RegisterEndpointNotificationCallback(nint callback) => 0;
        public int UnregisterEndpointNotificationCallback(nint callback) => 0;
    }

    private sealed class FakeCollection(FakeEndpoint[] endpoints) : IMultimediaDeviceCollection
    {
        public int GetCount(out uint count) { count = (uint)endpoints.Length; return 0; }
        public int Item(uint index, out IMultimediaDevice device) { device = endpoints[index]; return 0; }
    }

    private sealed class FakeEndpoint(string id, string name, Guid? container) : IMultimediaDevice, IAudioPropertyStore
    {
        public string Id => id;
        public AudioDeviceState State { get; init; } = AudioDeviceState.Active;
        public int StateResult { get; init; }
        public int PropertyOpens { get; private set; }
        public int NameReads { get; private set; }
        public int Activations { get; private set; }
        public int Activate(ref Guid iid, ClassContext context, nint parameters, out object activated)
        {
            Activations++;
            throw new InvalidOperationException("Probe must not activate volume controls");
        }
        public int OpenPropertyStore(uint mode, out IAudioPropertyStore properties)
        {
            PropertyOpens++;
            properties = this;
            return 0;
        }
        public int GetId(out string endpointId) { endpointId = id; return 0; }
        public int GetState(out AudioDeviceState state) { state = State; return StateResult; }
        public int GetValue(ref PropertyKey key, out PropVariant value)
        {
            value = default;
            if (key.PropertyId == 2 && container.HasValue)
            {
                value.VariantType = 72;
                value.PointerValue = Marshal.AllocCoTaskMem(16);
                Marshal.StructureToPtr(container.Value, value.PointerValue, false);
            }
            else if (key.PropertyId == 14)
            {
                NameReads++;
                value.VariantType = 31;
                value.PointerValue = Marshal.StringToCoTaskMemUni(name);
            }
            // The production PropVariantClear frees the CoTaskMem allocations.
            return 0;
        }
        public int GetCount(out uint count) { count = 2; return 0; }
        public int GetAt(uint index, out PropertyKey key) => throw new NotSupportedException();
        public int SetValue(ref PropertyKey key, ref PropVariant value) => throw new NotSupportedException();
        public int Commit() => throw new NotSupportedException();
    }
}
