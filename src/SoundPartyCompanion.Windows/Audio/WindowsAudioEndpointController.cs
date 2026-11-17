using System.Diagnostics;
using System.Runtime.InteropServices;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Windows.Audio;

public sealed class WindowsAudioEndpointController : IAudioEndpointController
{
    private readonly IAppLogger? _logger;
    private readonly Func<IMultimediaDeviceEnumerator> _createEnumerator;
    private readonly object _timingGate = new();
    private readonly Dictionary<string, (string Outcome, long LoggedAt)> _lastTimings = new();

    public WindowsAudioEndpointController(IAppLogger? logger = null) : this(CoreAudioInterop.CreateEnumerator, logger) { }

    internal WindowsAudioEndpointController(Func<IMultimediaDeviceEnumerator> createEnumerator, IAppLogger? logger = null)
    {
        _createEnumerator = createEnumerator;
        _logger = logger;
    }

    public ValueTask<bool> IsCurrentOutputAsync(SpeakerDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        var timing = new ProbeTiming("DefaultOutput");
        IMultimediaDeviceEnumerator? enumerator = null;
        IMultimediaDevice? endpoint = null;
        var endpointStarted = Stopwatch.GetTimestamp();
        try
        {
            if (!device.IsConnected)
            {
                timing.Outcome = "Offline";
                return ValueTask.FromResult(false);
            }
            enumerator = _createEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, AudioDeviceRole.Multimedia, out endpoint) < 0)
            {
                timing.Outcome = "NoDefault";
                return ValueTask.FromResult(false);
            }
            Marshal.ThrowExceptionForHR(endpoint.GetState(out var state));
            if (state != AudioDeviceState.Active)
            {
                timing.Outcome = "NoDefault";
                return ValueTask.FromResult(false);
            }

            var hasContainer = device.ContainerId.GetValueOrDefault() != Guid.Empty;
            timing.IdentityReads++;
            var (name, containerId) = CoreAudioInterop.GetEndpointIdentity(endpoint, hasContainer, device.ContainerId);
            bool isCurrent;
            if (hasContainer && containerId.HasValue)
            {
                // A container match identifies this single default endpoint without scanning others.
                isCurrent = containerId == device.ContainerId &&
                    AudioEndpointClassifier.Classify(name ?? string.Empty, AudioEndpointDirection.Render, true) ==
                        SpeakerAudioEndpointKind.A2dpRender;
            }
            else
            {
                // Preserve the unique-alias fallback when Windows has no container property.
                // Only inspect metadata; this path must never activate volume controls.
                var matches = EnumerateMatchingEndpoints(enumerator, device, cancellationToken, timing);
                isCurrent = AudioEndpointSelection.SelectA2dpEndpoint(matches)?.IsDefault == true;
            }
            timing.Outcome = isCurrent ? "Speaker" : "Other";
            return ValueTask.FromResult(isCurrent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timing.Outcome = "Canceled";
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(AppLogLevel.Warning, $"Default audio output probe failed ({exception.GetType().Name}).");
            return ValueTask.FromResult(false);
        }
        finally
        {
            timing.EndpointMs = ElapsedMilliseconds(endpointStarted);
            CoreAudioInterop.Release(endpoint);
            CoreAudioInterop.Release(enumerator);
            LogProbeTiming(timing);
        }
    }

    public IDisposable? WatchSpeakerEndpointActivations(Func<Guid?>? getSpeakerContainerId = null)
    {
        IMultimediaDeviceEnumerator? enumerator = null;
        nint callbackPointer = 0;
        try
        {
            enumerator = _createEnumerator();
            var callback = new PlaybackEndpointCallback(
                (endpoint, timestamp) => LogSpeakerEndpointActivation(endpoint, timestamp, getSpeakerContainerId?.Invoke()),
                (role, endpoint, timestamp) => LogDefaultPlaybackChange(role, endpoint, timestamp, getSpeakerContainerId?.Invoke()));
            // MMDevice does not retain the callback interface. Keep this reference until unregistering.
            callbackPointer = Marshal.GetComInterfaceForObject(callback, typeof(IMMNotificationClient));
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(callbackPointer));
            var subscription = new EndpointActivationSubscription(enumerator, callbackPointer);
            enumerator = null;
            callbackPointer = 0;
            _logger?.Write(AppLogLevel.Information, "Speaker endpoint activation watch started.");
            return subscription;
        }
        catch (Exception)
        {
            _logger?.Write(AppLogLevel.Warning, "Speaker endpoint activation watch failed.");
            return null;
        }
        finally
        {
            if (callbackPointer != 0) Marshal.Release(callbackPointer);
            CoreAudioInterop.Release(enumerator);
        }
    }

    private void LogSpeakerEndpointActivation(string endpointId, DateTimeOffset notifiedAt, Guid? speakerContainerId)
    {
        try
        {
            if (IsSupportedSpeakerPlaybackEndpoint(endpointId, speakerContainerId))
            {
                _logger?.Write(AppLogLevel.Information,
                    $"Speaker playback endpoint became active. EventUtc={notifiedAt.UtcDateTime:O}");
            }
        }
        catch (Exception)
        {
            _logger?.Write(AppLogLevel.Warning, "Audio endpoint activation lookup failed.");
        }
    }

    private void LogDefaultPlaybackChange(int role, string? endpointId, DateTimeOffset notifiedAt, Guid? speakerContainerId)
    {
        var target = "None";
        if (endpointId is not null)
        {
            try
            {
                target = IsSupportedSpeakerPlaybackEndpoint(endpointId, speakerContainerId) ? "Speaker" : "Other";
            }
            catch (Exception)
            {
                target = "Unknown";
            }
        }
        _logger?.Write(AppLogLevel.Information,
            $"Windows default playback changed. EventUtc={notifiedAt.UtcDateTime:O} Role={(AudioDeviceRole)role} Target={target}");
    }

    private bool IsSupportedSpeakerPlaybackEndpoint(string endpointId, Guid? speakerContainerId)
    {
        IMultimediaDeviceEnumerator? enumerator = null;
        IMultimediaDevice? endpoint = null;
        try
        {
            enumerator = _createEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out endpoint));
            Marshal.ThrowExceptionForHR(((IMMEndpoint)endpoint).GetDataFlow(out var flow));
            if (flow != AudioDataFlow.Render) return false;

            var (name, containerId) = CoreAudioInterop.GetEndpointIdentity(endpoint,
                speakerContainerId.GetValueOrDefault() != Guid.Empty, speakerContainerId);
            var sameContainer = containerId.HasValue && containerId == speakerContainerId;
            if (containerId.HasValue && !sameContainer) return false;

            return (sameContainer || (name is not null && AudioEndpointClassifier.IsSupportedSpeakerEndpointName(name))) &&
                AudioEndpointClassifier.Classify(name ?? string.Empty, AudioEndpointDirection.Render, sameContainer) ==
                    SpeakerAudioEndpointKind.A2dpRender;
        }
        finally
        {
            CoreAudioInterop.Release(endpoint);
            CoreAudioInterop.Release(enumerator);
        }
    }

    public ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        var timing = new ProbeTiming("Snapshot");
        IMultimediaDeviceEnumerator? enumerator = null;

        try
        {
            if (!device.IsConnected)
            {
                timing.Outcome = "Offline";
                return ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow));
            }
            IReadOnlyList<AudioEndpointDescriptor> endpoints;
            var endpointStarted = Stopwatch.GetTimestamp();
            try
            {
                enumerator = _createEnumerator();
                endpoints = EnumerateMatchingEndpoints(enumerator, device, cancellationToken, timing);
            }
            finally
            {
                timing.EndpointMs = ElapsedMilliseconds(endpointStarted);
            }
            var a2dpEndpoint = AudioEndpointSelection.SelectA2dpEndpoint(endpoints);

            if (a2dpEndpoint is null)
            {
                timing.Outcome = "NoEndpoint";
                return ValueTask.FromResult(new SpeakerAudioSnapshot(
                    endpoints,
                    null,
                    null,
                    false,
                    DateTimeOffset.UtcNow));
            }

            var controlsStarted = Stopwatch.GetTimestamp();
            var (volume, isMuted) = ReadControls(enumerator, a2dpEndpoint.EndpointId);
            timing.ControlsMs = ElapsedMilliseconds(controlsStarted);
            timing.Outcome = volume.HasValue && isMuted.HasValue ? "Ready" : "ReadFailed";
            return ValueTask.FromResult(new SpeakerAudioSnapshot(
                endpoints,
                volume,
                isMuted,
                a2dpEndpoint.IsDefault,
                DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timing.Outcome = "Canceled";
            throw;
        }
        catch (Exception exception)
        {
            LogSnapshotFailure(exception);
            return ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow));
        }
        finally
        {
            CoreAudioInterop.Release(enumerator);
            LogProbeTiming(timing);
        }
    }

    public IDisposable? WatchVolume(string endpointId, Action<double, bool> volumeChanged)
    {
        IMultimediaDeviceEnumerator? enumerator = null;
        IMultimediaDevice? endpoint = null;
        IAudioEndpointVolume? endpointVolume = null;
        try
        {
            enumerator = _createEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out endpoint));
            endpointVolume = CoreAudioInterop.ActivateEndpointVolume(endpoint);
            var callback = new EndpointVolumeCallback(volumeChanged);
            Marshal.ThrowExceptionForHR(endpointVolume.RegisterControlChangeNotify(callback));
            var subscription = new EndpointVolumeSubscription(endpointVolume, callback);
            endpointVolume = null;
            return subscription;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Audio endpoint volume notifications unavailable ({exception.GetType().Name}).");
            return null;
        }
        finally
        {
            CoreAudioInterop.Release(endpointVolume);
            CoreAudioInterop.Release(endpoint);
            CoreAudioInterop.Release(enumerator);
        }
    }

    public ValueTask<AudioControlResult> SetVolumeAsync(
        SpeakerDevice device,
        double volumePercent,
        CancellationToken cancellationToken = default) =>
        ChangeEndpointVolumeAsync(device, volumePercent, null, cancellationToken);

    public ValueTask<AudioControlResult> SetMutedAsync(
        SpeakerDevice device,
        bool isMuted,
        CancellationToken cancellationToken = default) =>
        ChangeEndpointVolumeAsync(device, null, isMuted, cancellationToken);

    public ValueTask<AudioControlResult> SetAsDefaultOutputAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        if (!device.IsConnected)
        {
            return ValueTask.FromResult(AudioControlResult.SpeakerOffline);
        }

        IMultimediaDeviceEnumerator? enumerator = null;
        object? policyObject = null;

        try
        {
            enumerator = _createEnumerator();
            var endpoints = EnumerateMatchingEndpoints(enumerator, device, cancellationToken);
            var a2dpEndpoint = AudioEndpointSelection.SelectA2dpEndpoint(endpoints);

            if (a2dpEndpoint is null)
            {
                return ValueTask.FromResult(AudioControlResult.EndpointNotFound);
            }

            policyObject = Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))!);
            var policy = (IPolicyConfig)(policyObject ??
                throw new COMException("Windows audio policy client is unavailable."));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(
                a2dpEndpoint.EndpointId,
                AudioDeviceRole.Console));
            Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(
                a2dpEndpoint.EndpointId,
                AudioDeviceRole.Multimedia));

            return ValueTask.FromResult(AudioControlResult.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Default audio output switch needs user action ({exception.GetType().Name}).");

            return ValueTask.FromResult(OpenSoundSettings()
                ? AudioControlResult.UserActionRequired
                : AudioControlResult.Failed);
        }
        finally
        {
            CoreAudioInterop.Release(policyObject);
            CoreAudioInterop.Release(enumerator);
        }
    }

    private ValueTask<AudioControlResult> ChangeEndpointVolumeAsync(
        SpeakerDevice device,
        double? volumePercent,
        bool? isMuted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        if (!device.IsConnected)
        {
            return ValueTask.FromResult(AudioControlResult.SpeakerOffline);
        }

        IMultimediaDeviceEnumerator? enumerator = null;
        IMultimediaDevice? endpoint = null;
        IAudioEndpointVolume? endpointVolume = null;

        try
        {
            enumerator = _createEnumerator();
            var endpoints = EnumerateMatchingEndpoints(enumerator, device, cancellationToken);
            var a2dpEndpoint = AudioEndpointSelection.SelectA2dpEndpoint(endpoints);

            if (a2dpEndpoint is null)
            {
                return ValueTask.FromResult(AudioControlResult.EndpointNotFound);
            }

            Marshal.ThrowExceptionForHR(enumerator.GetDevice(a2dpEndpoint.EndpointId, out endpoint));
            endpointVolume = CoreAudioInterop.ActivateEndpointVolume(endpoint);
            var eventContext = Guid.Empty;

            if (volumePercent.HasValue)
            {
                var scalar = (float)(AudioValueRules.ClampVolumePercent(volumePercent.Value) / 100);
                Marshal.ThrowExceptionForHR(endpointVolume.SetMasterVolumeLevelScalar(scalar, ref eventContext));
            }

            if (isMuted.HasValue)
            {
                Marshal.ThrowExceptionForHR(endpointVolume.SetMute(isMuted.Value, ref eventContext));
            }

            return ValueTask.FromResult(AudioControlResult.Success);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Audio endpoint control failed ({exception.GetType().Name}).");
            return ValueTask.FromResult(AudioControlResult.Failed);
        }
        finally
        {
            CoreAudioInterop.Release(endpointVolume);
            CoreAudioInterop.Release(endpoint);
            CoreAudioInterop.Release(enumerator);
        }
    }

    private IReadOnlyList<AudioEndpointDescriptor> EnumerateMatchingEndpoints(
        IMultimediaDeviceEnumerator enumerator,
        SpeakerDevice device,
        CancellationToken cancellationToken,
        ProbeTiming? timing = null)
    {
        var endpoints = new List<AudioEndpointDescriptor>();
        IMultimediaDeviceCollection? collection = null;
        IMultimediaDevice? defaultDevice = null;
        string? defaultEndpointId = null;
        Exception? endpointFailure = null;
        var readContainerId = device.ContainerId.GetValueOrDefault() != Guid.Empty;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(
                    AudioDataFlow.Render,
                    AudioDeviceRole.Multimedia,
                    out defaultDevice) >= 0 &&
                defaultDevice.GetId(out var id) >= 0)
            {
                defaultEndpointId = id;
            }

            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                AudioDataFlow.Render,
                AudioDeviceState.Active,
                out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMultimediaDevice? endpoint = null;

                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out endpoint));
                    // The endpoint may have disconnected since enumeration. Do not open its properties.
                    Marshal.ThrowExceptionForHR(endpoint.GetState(out var state));
                    if (state != AudioDeviceState.Active) continue;
                    if (timing is not null) timing.IdentityReads++;
                    var (friendlyName, containerId) = CoreAudioInterop.GetEndpointIdentity(endpoint, readContainerId, device.ContainerId);
                    var sameContainer = containerId.HasValue && containerId == device.ContainerId;
                    if (!sameContainer &&
                        (containerId.HasValue || string.IsNullOrWhiteSpace(friendlyName) ||
                         (!AudioEndpointClassifier.NamesMatch(device.DisplayName, friendlyName) &&
                          !AudioEndpointClassifier.MatchesSupportedSpeakerAlias(device.DisplayName, friendlyName))))
                    {
                        continue;
                    }

                    var endpointId = CoreAudioInterop.GetEndpointId(endpoint);
                    var endpointName = friendlyName ?? string.Empty;
                    endpoints.Add(new AudioEndpointDescriptor(
                        endpointId,
                        endpointName,
                        AudioEndpointDirection.Render,
                        AudioEndpointClassifier.Classify(endpointName, AudioEndpointDirection.Render),
                        state == AudioDeviceState.Active,
                        string.Equals(endpointId, defaultEndpointId, StringComparison.Ordinal)) { ContainerId = containerId });
                }
                catch (Exception exception)
                {
                    // A missing or unreadable device can coexist with a usable speaker.
                    endpointFailure ??= exception;
                }
                finally
                {
                    CoreAudioInterop.Release(endpoint);
                }
            }

            var matches = AudioEndpointSelection.MatchSpeakerEndpoints(device.DisplayName, endpoints, device.ContainerId);
            if (AudioEndpointSelection.SelectA2dpEndpoint(matches) is null && endpointFailure is not null)
            {
                LogSnapshotFailure(endpointFailure);
            }
            return matches;
        }
        finally
        {
            CoreAudioInterop.Release(defaultDevice);
            CoreAudioInterop.Release(collection);
        }
    }

    private (double? VolumePercent, bool? IsMuted) ReadControls(
        IMultimediaDeviceEnumerator enumerator,
        string endpointId)
    {
        IMultimediaDevice? endpoint = null;
        IAudioEndpointVolume? endpointVolume = null;

        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out endpoint));
            endpointVolume = CoreAudioInterop.ActivateEndpointVolume(endpoint);
            var volumeResult = endpointVolume.GetMasterVolumeLevelScalar(out var scalar);
            var muteResult = endpointVolume.GetMute(out var isMuted);
            LogSnapshotFailure(Marshal.GetExceptionForHR(volumeResult < 0 ? volumeResult : muteResult, new nint(-1)));
            return (
                volumeResult >= 0 ? AudioValueRules.ClampVolumePercent(scalar * 100) : null,
                muteResult >= 0 ? isMuted : null);
        }
        catch (Exception exception)
        {
            LogSnapshotFailure(exception);
            return (null, null);
        }
        finally
        {
            CoreAudioInterop.Release(endpointVolume);
            CoreAudioInterop.Release(endpoint);
        }
    }

    private void LogSnapshotFailure(Exception? exception)
    {
        if (exception is not null)
        {
            _logger?.Write(AppLogLevel.Warning, $"Audio endpoint snapshot failed ({exception.GetType().Name}).");
        }
    }

    private static long ElapsedMilliseconds(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private sealed class ProbeTiming(string operation)
    {
        public string Operation { get; } = operation;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public long Started { get; } = Stopwatch.GetTimestamp();
        public long EndpointMs { get; set; }
        public long ControlsMs { get; set; }
        public int IdentityReads { get; set; }
        public string Outcome { get; set; } = "Failed";
    }

    private void LogProbeTiming(ProbeTiming timing)
    {
        if (_logger is null) return;
        var duration = ElapsedMilliseconds(timing.Started);
        lock (_timingGate)
        {
            // Log transitions immediately; bound repeated slow probes and healthy heartbeats.
            if (_lastTimings.TryGetValue(timing.Operation, out var previous) && previous.Outcome == timing.Outcome &&
                Stopwatch.GetElapsedTime(previous.LoggedAt) < TimeSpan.FromSeconds(duration >= 250 ? 5 : 30)) return;
            _lastTimings[timing.Operation] = (timing.Outcome, Stopwatch.GetTimestamp());
        }
        _logger.Write(AppLogLevel.Information, FormattableString.Invariant(
            $"Audio probe completed. Operation={timing.Operation} StartedUtc={timing.StartedUtc:O} DurationMs={duration} EndpointMs={timing.EndpointMs} ControlsMs={timing.ControlsMs} IdentityReads={timing.IdentityReads} Outcome={timing.Outcome}"));
    }

    private static bool OpenSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class PlaybackEndpointCallback(
        Action<string, DateTimeOffset> activation,
        Action<int, string?, DateTimeOffset> defaultChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string endpointId, uint state)
        {
            if (state == (uint)AudioDeviceState.Active)
            {
                var notifiedAt = DateTimeOffset.UtcNow;
                ThreadPool.QueueUserWorkItem(_ => activation(endpointId, notifiedAt));
            }
            return 0;
        }

        public int OnDeviceAdded(string endpointId) => 0;
        public int OnDeviceRemoved(string endpointId) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? endpointId)
        {
            if (flow == (int)AudioDataFlow.Render &&
                (AudioDeviceRole)role is AudioDeviceRole.Console or AudioDeviceRole.Multimedia or AudioDeviceRole.Communications)
            {
                var notifiedAt = DateTimeOffset.UtcNow;
                ThreadPool.QueueUserWorkItem(_ => defaultChanged(role, endpointId, notifiedAt));
            }
            return 0;
        }
        public int OnPropertyValueChanged(string endpointId, PropertyKey key) => 0;
    }

    private sealed class EndpointActivationSubscription(IMultimediaDeviceEnumerator enumerator, nint callbackPointer) : IDisposable
    {
        private nint _callbackPointer = callbackPointer;

        public void Dispose()
        {
            var callback = Interlocked.Exchange(ref _callbackPointer, 0);
            if (callback == 0) return;
            try
            {
                enumerator.UnregisterEndpointNotificationCallback(callback);
            }
            finally
            {
                try { CoreAudioInterop.Release(enumerator); }
                finally { Marshal.Release(callback); }
            }
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class EndpointVolumeCallback(Action<double, bool> volumeChanged) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notification)
        {
            if (notification == nint.Zero) return 0;

            try
            {
                var value = Marshal.PtrToStructure<AudioVolumeNotification>(notification);
                volumeChanged(AudioValueRules.ClampVolumePercent(value.MasterVolume * 100), value.IsMuted != 0);
                return 0;
            }
            catch (Exception exception)
            {
                return Marshal.GetHRForException(exception);
            }
        }
    }

    private sealed class EndpointVolumeSubscription(
        IAudioEndpointVolume endpointVolume,
        IAudioEndpointVolumeCallback callback) : IDisposable
    {
        private IAudioEndpointVolume? _endpointVolume = endpointVolume;

        public void Dispose()
        {
            var volume = Interlocked.Exchange(ref _endpointVolume, null);
            if (volume is null) return;

            try
            {
                volume.UnregisterControlChangeNotify(callback);
            }
            finally
            {
                CoreAudioInterop.Release(volume);
            }
        }
    }
}
