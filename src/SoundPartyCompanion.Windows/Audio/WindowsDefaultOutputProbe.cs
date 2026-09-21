using SoundPartyCompanion.Core.Audio;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Windows.Audio;

public sealed class WindowsDefaultOutputProbe : IOutputDeviceProbe
{
    private readonly IAudioEndpointController _audioController;
    private readonly IAppLogger? _logger;

    public WindowsDefaultOutputProbe(IAppLogger? logger = null)
        : this(new WindowsAudioEndpointController(logger), logger)
    {
    }

    public WindowsDefaultOutputProbe(
        IAudioEndpointController audioController,
        IAppLogger? logger = null)
    {
        _audioController = audioController ?? throw new ArgumentNullException(nameof(audioController));
        _logger = logger;
    }

    public async ValueTask<bool> IsCurrentOutputAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var snapshot = await _audioController
                .GetSnapshotAsync(device, cancellationToken)
                .ConfigureAwait(false);
            return snapshot.IsCurrentOutput;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Default audio output probe failed ({exception.GetType().Name}).");
            return false;
        }
    }

}
