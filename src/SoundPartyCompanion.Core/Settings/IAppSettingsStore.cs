namespace SoundPartyCompanion.Core.Settings;

public interface IAppSettingsStore
{
    ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IStartupManager
{
    bool IsEnabled { get; }

    ValueTask<bool> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default);
}
