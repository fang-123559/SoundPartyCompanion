using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SoundPartyCompanion.Core.Devices;

namespace SoundPartyCompanion.Core.Diagnostics;

public sealed record DiagnosticExportSnapshot(
    Version AppVersion,
    Version OsVersion,
    Architecture Architecture,
    SpeakerConnectionState ConnectionState,
    int? BatteryPercent,
    bool BatteryIsStale,
    DateTimeOffset? BatteryObservedAt,
    double? VolumePercent,
    bool? IsMuted,
    bool IsSynthetic = false);

public sealed record DiagnosticExportResult(int IncludedLogs, int SkippedLogs, int TruncatedLogs);

public sealed class DiagnosticExportService
{
    public const int MaximumBytesPerLog = 1024 * 1024;
    private static readonly string[] LogNames =
        ["application.log", "application.log.1", "application.log.2", "application.log.3"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<DiagnosticExportResult> ExportAsync(
        string logDirectory,
        string destinationPath,
        DiagnosticExportSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.AppVersion);
        ArgumentNullException.ThrowIfNull(snapshot.OsVersion);
        cancellationToken.ThrowIfCancellationRequested();
        var logsRoot = Path.GetFullPath(logDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Diagnostics must be exported to a ZIP file.", nameof(destinationPath));
        if (destination.StartsWith(logsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export destination must be outside the source log directory.", nameof(destinationPath));
        if (!Enum.IsDefined(snapshot.Architecture) || !Enum.IsDefined(snapshot.ConnectionState))
            throw new ArgumentException("Invalid diagnostic snapshot.", nameof(snapshot));
        if (snapshot.BatteryPercent is < 0 or > 100 ||
            snapshot.VolumePercent is { } volume && (!double.IsFinite(volume) || volume is < 0 or > 100))
            throw new ArgumentException("Invalid diagnostic percentage.", nameof(snapshot));

        var included = 0;
        var skipped = 0;
        var truncated = 0;
        var logStatuses = new List<object>();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var directoryIsLink = Directory.Exists(logsRoot) &&
                (File.GetAttributes(logsRoot) & FileAttributes.ReparsePoint) != 0;
            foreach (var name in LogNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (directoryIsLink)
                {
                    skipped++;
                    logStatuses.Add(new { name, status = "skipped-link", truncated = false });
                    continue;
                }
                try
                {
                    var path = Path.Combine(logsRoot, name);
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped++;
                        logStatuses.Add(new { name, status = "skipped-link", truncated = false });
                        continue;
                    }
                    var (content, wasTruncated) = await ReadLogTailAsync(path, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, "logs/" + name, LogPrivacyFilter.FilterForExport(content), cancellationToken)
                        .ConfigureAwait(false);
                    included++;
                    if (wasTruncated) truncated++;
                    logStatuses.Add(new { name, status = "included", truncated = wasTruncated });
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    logStatuses.Add(new { name, status = "missing", truncated = false });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    logStatuses.Add(new { name, status = "unreadable", truncated = false });
                }
            }
            var metadata = new
            {
                schemaVersion = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                appVersion = snapshot.AppVersion.ToString(),
                osVersion = snapshot.OsVersion.ToString(),
                snapshot.Architecture,
                snapshot.ConnectionState,
                snapshot.BatteryPercent,
                snapshot.BatteryIsStale,
                batteryObservedAtUtc = snapshot.BatteryObservedAt?.ToUniversalTime(),
                snapshot.VolumePercent,
                snapshot.IsMuted,
                snapshot.IsSynthetic,
                logStatuses,
                privacy = "Only allowlisted events; unknown text and legacy exception details omitted. No settings, selection cache, media metadata or device identifiers."
            };
            await WriteEntryAsync(archive, "diagnostics.json", JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken)
                .ConfigureAwait(false);
            await WriteEntryAsync(archive, "README.txt",
                "Sound Party Companion 本地诊断包\n" +
                "仅包含版本、连接/音频/电量基础状态与二次脱敏日志，不会自动上传。\n" +
                "不包含设备选择缓存、配置文件、蓝牙地址、设备标识、用户名、个人路径或歌曲/歌手。\n" +
                "只保留已知事件模板；未知文本与旧异常详情被省略。\n" +
                "每份日志最多保留最后 1 MiB，缺失、不可读或链接文件的处理结果见 diagnostics.json。\n" +
                "电量过期时仍标为缓存，不代表实时读数；充电识别未启用。\n" +
                "分享前请自行检查包内内容。\n", cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Never overwrite another file. Build the complete archive before opening the destination.
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        try
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await output.DisposeAsync().ConfigureAwait(false);
            try { File.Delete(destination); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return new DiagnosticExportResult(included, skipped, truncated);
    }

    private static async Task<(string Content, bool Truncated)> ReadLogTailAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920, useAsync: true);
        var start = Math.Max(0, input.Length - MaximumBytesPerLog);
        input.Position = start;
        var bytes = new byte[(int)Math.Min(input.Length - start, MaximumBytesPerLog)];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = await input.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }
        var text = Encoding.UTF8.GetString(bytes, 0, read);
        if (start > 0)
        {
            var newline = text.IndexOf('\n');
            text = newline < 0 ? string.Empty : text[(newline + 1)..];
        }
        return (text, start > 0);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
