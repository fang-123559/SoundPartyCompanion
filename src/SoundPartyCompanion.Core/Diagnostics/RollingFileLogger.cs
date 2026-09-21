using System.Text;

namespace SoundPartyCompanion.Core.Diagnostics;

public sealed class RollingFileLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly string _logFilePath;
    private readonly long _maximumBytes;
    private readonly int _retainedFiles;

    public RollingFileLogger(
        string logDirectory,
        string fileName = "application.log",
        long maximumBytes = 5 * 1024 * 1024,
        int retainedFiles = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        if (retainedFiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        }

        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, fileName);
        _maximumBytes = maximumBytes;
        _retainedFiles = retainedFiles;
    }

    public string LogFilePath => _logFilePath;

    public void Write(AppLogLevel level, string message, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        var entry = FormatEntry(level, message, exception);
        lock (_sync)
        {
            RotateIfRequired(Encoding.UTF8.GetByteCount(entry));
            File.AppendAllText(_logFilePath, entry, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string FormatEntry(AppLogLevel level, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .AppendLine(message.ReplaceLineEndings(" "));

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        return builder.ToString();
    }

    private void RotateIfRequired(int pendingBytes)
    {
        if (!File.Exists(_logFilePath))
        {
            return;
        }

        var currentLength = new FileInfo(_logFilePath).Length;
        if (currentLength + pendingBytes <= _maximumBytes)
        {
            return;
        }

        for (var index = _retainedFiles; index >= 1; index--)
        {
            var destination = $"{_logFilePath}.{index}";
            var source = index == 1 ? _logFilePath : $"{_logFilePath}.{index - 1}";

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }
}

