using System.Text.Json;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;

namespace SoundPartyCompanion.Windows.Storage;

public sealed class LocalSpeakerSelectionStore : ISpeakerSelectionStore
{
    private readonly string _filePath;
    private readonly IAppLogger? _logger;

    public LocalSpeakerSelectionStore(IAppLogger? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoundPartyCompanion",
            "speaker-selection.json");
    }

    public async ValueTask<SpeakerSelection?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var selection = await JsonSerializer.DeserializeAsync<SpeakerSelection>(
                stream,
                cancellationToken: cancellationToken);

            return IsValid(selection) ? selection : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Local speaker selection could not be loaded ({exception.GetType().Name}).");
            return null;
        }
    }

    public async ValueTask SaveAsync(
        SpeakerSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (!IsValid(selection))
        {
            throw new ArgumentException("The speaker selection is invalid.", nameof(selection));
        }

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The selection path has no parent directory.");
        Directory.CreateDirectory(directory);

        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await JsonSerializer.SerializeAsync(
                stream,
                selection,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Local speaker selection could not be saved ({exception.GetType().Name}).");
        }
    }

    private static bool IsValid(SpeakerSelection? selection) =>
        selection is not null &&
        selection.DeviceKey.StartsWith("bt-", StringComparison.Ordinal) &&
        selection.DeviceKey.Length == 35 &&
        !string.IsNullOrWhiteSpace(selection.DisplayName);
}
