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
            var a2dpEndpoint = SelectA2dpEndpoint(endpoints);

            if (a2dpEndpoint is null)
            {
                return ValueTask.FromResult(new SpeakerAudioSnapshot(
                    endpoints,
                    null,
                    false,
                    false,
                    DateTimeOffset.UtcNow));
            }

            var (volume, isMuted) = ReadVolume(enumerator, a2dpEndpoint.EndpointId);
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
            _logger?.Write(
                AppLogLevel.Warning,
                $"Audio endpoint snapshot failed ({exception.GetType().Name}).");
            return ValueTask.FromResult(SpeakerAudioSnapshot.Unavailable(DateTimeOffset.UtcNow));
        }
        finally
        {
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
            var a2dpEndpoint = SelectA2dpEndpoint(endpoints);

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
            var a2dpEndpoint = SelectA2dpEndpoint(endpoints);

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

    private static IReadOnlyList<AudioEndpointDescriptor> EnumerateMatchingEndpoints(
        IMultimediaDeviceEnumerator enumerator,
        string deviceName,
        CancellationToken cancellationToken)
    {
        var endpoints = new List<AudioEndpointDescriptor>();
        EnumerateDirection(
            enumerator,
            deviceName,
            AudioDataFlow.Render,
            AudioEndpointDirection.Render,
            endpoints,
            cancellationToken);
        EnumerateDirection(
            enumerator,
            deviceName,
            AudioDataFlow.Capture,
            AudioEndpointDirection.Capture,
            endpoints,
            cancellationToken);
        return endpoints;
    }

    private static void EnumerateDirection(
        IMultimediaDeviceEnumerator enumerator,
        string deviceName,
        AudioDataFlow dataFlow,
        AudioEndpointDirection direction,
        ICollection<AudioEndpointDescriptor> endpoints,
        CancellationToken cancellationToken)
    {
        IMultimediaDeviceCollection? collection = null;
        IMultimediaDevice? defaultDevice = null;
        string? defaultEndpointId = null;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(
                    dataFlow,
                    AudioDeviceRole.Multimedia,
                    out defaultDevice) >= 0)
            {
                defaultEndpointId = CoreAudioInterop.GetEndpointId(defaultDevice);
            }

            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                dataFlow,
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
                        !AudioEndpointClassifier.NamesMatch(deviceName, friendlyName))
                    {
                        continue;
                    }

                    Marshal.ThrowExceptionForHR(endpoint.GetState(out var state));
                    var endpointId = CoreAudioInterop.GetEndpointId(endpoint);
                    endpoints.Add(new AudioEndpointDescriptor(
                        endpointId,
                        friendlyName,
                        direction,
                        AudioEndpointClassifier.Classify(friendlyName, direction),
                        state == AudioDeviceState.Active,
                        string.Equals(endpointId, defaultEndpointId, StringComparison.Ordinal)));
                }
                finally
                {
                    CoreAudioInterop.Release(endpoint);
                }
            }
        }
        finally
        {
            CoreAudioInterop.Release(defaultDevice);
            CoreAudioInterop.Release(collection);
        }
    }

    private static AudioEndpointDescriptor? SelectA2dpEndpoint(
        IEnumerable<AudioEndpointDescriptor> endpoints) =>
        endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender &&
            endpoint.IsActive &&
            endpoint.IsDefault)
        ?? endpoints.FirstOrDefault(endpoint =>
            endpoint.Kind == SpeakerAudioEndpointKind.A2dpRender && endpoint.IsActive);

    private static (double VolumePercent, bool IsMuted) ReadVolume(
        IMultimediaDeviceEnumerator enumerator,
        string endpointId)
    {
        IMultimediaDevice? endpoint = null;
        IAudioEndpointVolume? endpointVolume = null;

        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out endpoint));
            endpointVolume = CoreAudioInterop.ActivateEndpointVolume(endpoint);
            Marshal.ThrowExceptionForHR(endpointVolume.GetMasterVolumeLevelScalar(out var scalar));
            Marshal.ThrowExceptionForHR(endpointVolume.GetMute(out var isMuted));
            return (AudioValueRules.ClampVolumePercent(scalar * 100), isMuted);
        }
        finally
        {
            CoreAudioInterop.Release(endpointVolume);
            CoreAudioInterop.Release(endpoint);
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
}
