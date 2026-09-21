using Microsoft.Win32;
using SoundPartyCompanion.Core.Diagnostics;
using SoundPartyCompanion.Core.Settings;

namespace SoundPartyCompanion.Windows.Startup;

public sealed class WindowsStartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SoundPartyCompanion";
    private readonly string _executablePath;
    private readonly IAppLogger? _logger;

    public WindowsStartupManager(string executablePath, IAppLogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        _executablePath = Path.GetFullPath(executablePath);
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var value = key?.GetValue(ValueName) as string;
                return string.Equals(
                    NormalizeCommand(value),
                    NormalizeCommand(BuildCommand()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                _logger?.Write(
                    AppLogLevel.Warning,
                    $"Startup state could not be read ({exception.GetType().Name}).");
                return false;
            }
        }
    }

    public ValueTask<bool> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (isEnabled)
            {
                key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return ValueTask.FromResult(true);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            _logger?.Write(
                AppLogLevel.Warning,
                $"Startup state could not be changed ({exception.GetType().Name}).");
            return ValueTask.FromResult(false);
        }
    }

    private string BuildCommand() => $"\"{_executablePath}\" --startup";

    private static string? NormalizeCommand(string? value) =>
        value?.Trim();
}
