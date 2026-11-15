using System.Diagnostics;
using System.Runtime.InteropServices;
using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Windows.Audio;

public sealed class WindowsAudioEndpointController(IAppLogger? logger = null) : IAudioEndpointController
{
    private readonly IAppLogger? _logger = logger;

    public ValueTask<SpeakerAudioSnapshot> GetSnapshotAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        if (!device.IsConnected)
        {
            return ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow));
        }

        IMultimediaDeviceEnumerator? enumerator = null;

        try
        {
            enumerator = CoreAudioInterop.CreateEnumerator();
            var endpoints = EnumerateMatchingEndpoints(enumerator, device.DisplayName, cancellationToken);
            var a2dpEndpoint = AudioEndpointSelection.SelectA2dpEndpoint(endpoints);

            if (a2dpEndpoint is null)
            {
                return ValueTask.FromResult(new SpeakerAudioSnapshot(
                    endpoints,
                    null,
                    null,
                    false,
                    DateTimeOffset.UtcNow));
            }

            var (volume, isMuted) = ReadControls(enumerator, a2dpEndpoint.EndpointId);
            return ValueTask.FromResult(new SpeakerAudioSnapshot(
                endpoints,
                volume,
                isMuted,
                a2dpEndpoint.IsDefault,
                DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
        }
    }

    public IDisposable? WatchVolume(string endpointId, Action<double, bool> volumeChanged)
    {
        IMultimediaDeviceEnumerator? enumerator = null;
        IMultimediaDevice? endpoint = null;
        IAudioEndpointVolume? endpointVolume = null;
        try
        {
            enumerator = CoreAudioInterop.CreateEnumerator();
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
            enumerator = CoreAudioInterop.CreateEnumerator();
            var endpoints = EnumerateMatchingEndpoints(enumerator, device.DisplayName, cancellationToken);
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
            enumerator = CoreAudioInterop.CreateEnumerator();
            var endpoints = EnumerateMatchingEndpoints(enumerator, device.DisplayName, cancellationToken);
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
        string deviceName,
        CancellationToken cancellationToken)
    {
        var endpoints = new List<AudioEndpointDescriptor>();
        IMultimediaDeviceCollection? collection = null;
        IMultimediaDevice? defaultDevice = null;
        string? defaultEndpointId = null;
        Exception? endpointFailure = null;

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
                AudioDeviceState.All,
                out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMultimediaDevice? endpoint = null;

                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(index, out endpoint));
                    var friendlyName = CoreAudioInterop.GetFriendlyName(endpoint);

                    if (string.IsNullOrWhiteSpace(friendlyName) ||
                        (!AudioEndpointClassifier.NamesMatch(deviceName, friendlyName) &&
                         !AudioEndpointClassifier.MatchesSupportedSpeakerAlias(deviceName, friendlyName)))
                    {
                        continue;
                    }

                    Marshal.ThrowExceptionForHR(endpoint.GetState(out var state));
                    var endpointId = CoreAudioInterop.GetEndpointId(endpoint);
                    endpoints.Add(new AudioEndpointDescriptor(
                        endpointId,
                        friendlyName,
                        AudioEndpointDirection.Render,
                        AudioEndpointClassifier.Classify(friendlyName, AudioEndpointDirection.Render),
                        state == AudioDeviceState.Active,
                        string.Equals(endpointId, defaultEndpointId, StringComparison.Ordinal)));
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

            var matches = AudioEndpointSelection.MatchSpeakerEndpoints(deviceName, endpoints);
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
