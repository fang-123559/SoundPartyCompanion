using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using SoundPartyCompanion.Core.Devices;
using SoundPartyCompanion.Core.Diagnostics;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class DiagnosticExportTests
{
    private const string Stamp = "2026-10-03T12:00:00.0000000+08:00";
    private static DiagnosticExportSnapshot Snapshot => new(
        new Version(0, 1, 0), new Version(10, 0, 22631), Architecture.X64,
        SpeakerConnectionState.ConnectedAsOutput, 30, false,
        DateTimeOffset.Parse(Stamp), 16, false, IsSynthetic: true);

    [Theory]
    [InlineData(@"E:\SyntheticUser\Desktop\private.txt", "SyntheticUser")]
    [InlineData(@"\\synthetic-server\private\file.txt", "synthetic-server")]
    [InlineData("/home/synthetic-user/private.txt", "synthetic-user")]
    [InlineData("password=synthetic-password", "synthetic-password")]
    [InlineData("\"token\":\"synthetic-token\"", "synthetic-token")]
    [InlineData("Bearer synthetic-bearer", "synthetic-bearer")]
    [InlineData("artist=synthetic-song-owner", "synthetic-song-owner")]
    [InlineData("synthetic@example.invalid", "example.invalid")]
    [InlineData("192.0.2.1", "192.0.2.1")]
    public void RedactText_RemovesSensitiveForms(string input, string forbidden)
    {
        Assert.DoesNotContain(forbidden, LogPrivacyFilter.RedactText(input, "nonmatching-user"));
    }

    [Fact]
    public void RedactText_RemovesSyntheticAddressesDeviceKeysAndUserNames()
    {
        var address = string.Join(":", Enumerable.Range(1, 6).Select(number => number.ToString("X2")));
        var compact = address.Replace(":", string.Empty);
        var deviceKey = "bt-" + new string('a', 32);
        var guid = Guid.NewGuid().ToString();
        var content = LogPrivacyFilter.RedactText(
            $"{address} {compact} Dev_{compact} {deviceKey} {guid} SyntheticUser", "SyntheticUser");
        foreach (var value in new[] { address, compact, deviceKey, guid, "SyntheticUser" })
            Assert.DoesNotContain(value, content);
    }

    [Fact]
    public void FilterForExport_OmitsUnknownMediaAndLegacyExceptionDetails()
    {
        var content = $"{Stamp} [Information] Application starting.\n" +
            $"{Stamp} [Warning] Media session snapshot failed (IOException).\n" +
            $"{Stamp} [Error] Now playing synthetic private song by synthetic private artist\n" +
            $"System.Exception: synthetic exception content\n   at E:\\SyntheticUser\\private.cs:line 1\n" +
            $"{Stamp} [Error] Unhandled UI exception. ExceptionType=IOException; HResult=0x80070002\n";
        var filtered = LogPrivacyFilter.FilterForExport(content);
        Assert.Contains("Application starting.", filtered);
        Assert.Contains("Media session snapshot failed (IOException).", filtered);
        Assert.Contains("HResult=0x80070002", filtered);
        Assert.Contains("[omitted: non-allowlisted event]", filtered);
        Assert.Contains("2026-10-03T04:00:00.0000000+00:00", filtered);
        Assert.DoesNotContain("synthetic", filtered);
        Assert.DoesNotContain("private.cs", filtered);
    }

    [Theory]
    [InlineData("Application starting. private appendix")]
    [InlineData("Media session snapshot failed (IOException). title=synthetic")]
    [InlineData("Application exiting with code 0. synthetic user")]
    public void FilterForExport_RejectsSensitiveAppendicesToSafePrefixes(string message)
    {
        var filtered = LogPrivacyFilter.FilterForExport($"{Stamp} [Information] {message}");
        Assert.Contains("[omitted: non-allowlisted event]", filtered);
        Assert.DoesNotContain(message, filtered);
    }

    [Fact]
    public void FilterForExport_DropsMalformedAndOversizedLines()
    {
        Assert.Empty(LogPrivacyFilter.FilterForExport("not a log entry\n" + new string('x', 9000)));
        Assert.Empty(LogPrivacyFilter.FilterForExport("9999-99-99T12:00:00Z [Error] Application starting."));
    }

    [Fact]
    public async Task Export_ContainsOnlyAllowlistedFilesAndStructuredState()
    {
        using var fixture = new ExportFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "application.log"),
            $"{Stamp} [Information] Application starting.\n{Stamp} [Error] private synthetic song\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "application.log.1"),
            $"{Stamp} [Warning] Battery refresh failed (IOException).\n");
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "other.log"), "synthetic excluded data");
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "settings.json"), "synthetic excluded settings");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "speaker-selection.json"), "synthetic excluded identity");

        var result = await new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot);
        Assert.Equal(new DiagnosticExportResult(2, 0, 0), result);
        using var archive = ZipFile.OpenRead(fixture.Destination);
        Assert.Equal(new[] { "README.txt", "diagnostics.json", "logs/application.log", "logs/application.log.1" },
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray());
        var text = await ReadAllEntriesAsync(archive);
        Assert.DoesNotContain("synthetic excluded", text);
        Assert.DoesNotContain("private synthetic song", text);
        Assert.DoesNotContain(fixture.Root, text);
        using var metadata = JsonDocument.Parse(await ReadEntryAsync(archive, "diagnostics.json"));
        Assert.Equal("ConnectedAsOutput", metadata.RootElement.GetProperty("connectionState").GetString());
        Assert.Equal(30, metadata.RootElement.GetProperty("batteryPercent").GetInt32());
        Assert.True(metadata.RootElement.GetProperty("isSynthetic").GetBoolean());
        Assert.False(metadata.RootElement.TryGetProperty("deviceName", out _));
        Assert.False(metadata.RootElement.TryGetProperty("deviceKey", out _));
        Assert.False(metadata.RootElement.TryGetProperty("trackTitle", out _));
    }

    [Fact]
    public async Task Export_HandlesMissingLogsAndPreservesUnknownState()
    {
        using var fixture = new ExportFixture();
        var snapshot = Snapshot with
        {
            ConnectionState = SpeakerConnectionState.PairedOffline,
            BatteryPercent = null, BatteryIsStale = true,
            BatteryObservedAt = null, VolumePercent = null, IsMuted = null
        };
        var result = await new DiagnosticExportService().ExportAsync(Path.Combine(fixture.Root, "missing"),
            fixture.Destination, snapshot);
        Assert.Equal(new DiagnosticExportResult(0, 0, 0), result);
        using var archive = ZipFile.OpenRead(fixture.Destination);
        using var metadata = JsonDocument.Parse(await ReadEntryAsync(archive, "diagnostics.json"));
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("batteryPercent").ValueKind);
        Assert.True(metadata.RootElement.GetProperty("batteryIsStale").GetBoolean());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("volumePercent").ValueKind);
    }

    [Fact]
    public async Task Export_SkipsLockedLogsAndStillCreatesValidArchive()
    {
        using var fixture = new ExportFixture();
        var path = Path.Combine(fixture.Logs, "application.log");
        await File.WriteAllTextAsync(path, $"{Stamp} [Information] Application starting.\n");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot);
        Assert.Equal(new DiagnosticExportResult(0, 1, 0), result);
        using var archive = ZipFile.OpenRead(fixture.Destination);
        Assert.NotNull(archive.GetEntry("diagnostics.json"));
        Assert.Null(archive.GetEntry("logs/application.log"));
    }

    [Fact]
    public async Task Export_ReadsLogsWhileWriterSharesTheFile()
    {
        using var fixture = new ExportFixture();
        var path = Path.Combine(fixture.Logs, "application.log");
        await File.WriteAllTextAsync(path, $"{Stamp} [Information] Application starting.\n");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var result = await new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot);
        Assert.Equal(1, result.IncludedLogs);
    }

    [Fact]
    public async Task Export_LimitsLargeLogsToTheirTail()
    {
        using var fixture = new ExportFixture();
        var log = $"{Stamp} [Information] Application starting.\n" +
            new string('x', DiagnosticExportService.MaximumBytesPerLog + 100) + "\n" +
            $"{Stamp} [Warning] Battery refresh failed (IOException).\n";
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "application.log"), log);
        var result = await new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot);
        Assert.Equal(1, result.TruncatedLogs);
        using var archive = ZipFile.OpenRead(fixture.Destination);
        var text = await ReadEntryAsync(archive, "logs/application.log");
        Assert.Contains("Battery refresh failed", text);
        Assert.DoesNotContain("Application starting.", text);
        Assert.True(text.Length < DiagnosticExportService.MaximumBytesPerLog);
    }

    [Fact]
    public async Task Export_NeverOverwritesAnExistingFile()
    {
        using var fixture = new ExportFixture();
        await File.WriteAllTextAsync(fixture.Destination, "synthetic existing data");
        await Assert.ThrowsAsync<IOException>(() =>
            new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot));
        Assert.Equal("synthetic existing data", await File.ReadAllTextAsync(fixture.Destination));
    }

    [Fact]
    public async Task Export_RejectsDestinationInsideSourceLogs()
    {
        using var fixture = new ExportFixture();
        var destination = Path.Combine(fixture.Logs, "export.zip");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DiagnosticExportService().ExportAsync(fixture.Logs, destination, Snapshot));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Export_CancellationCreatesNoOutput()
    {
        using var fixture = new ExportFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination, Snapshot, cancellation.Token));
        Assert.False(File.Exists(fixture.Destination));
    }

    [Fact]
    public async Task Export_FailedDestinationLeavesSourceIntact()
    {
        using var fixture = new ExportFixture();
        var path = Path.Combine(fixture.Logs, "application.log");
        var source = $"{Stamp} [Information] Application starting.\n";
        await File.WriteAllTextAsync(path, source);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new DiagnosticExportService().ExportAsync(
            fixture.Logs, Path.Combine(fixture.Root, "missing", "output.zip"), Snapshot));
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Export_InvalidSnapshotCreatesNoOutput()
    {
        using var fixture = new ExportFixture();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DiagnosticExportService().ExportAsync(fixture.Logs, fixture.Destination,
                Snapshot with { VolumePercent = double.NaN }));
        Assert.False(File.Exists(fixture.Destination));
    }

    [Fact]
    public async Task Export_CreatesClearlyMarkedSyntheticExamplePackage()
    {
        using var fixture = new ExportFixture();
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SoundPartyCompanion.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        var examples = Path.Combine(current!.FullName, "artifacts", "diagnostic-exports");
        Directory.CreateDirectory(examples);
        var output = Path.Combine(examples, $"synthetic-diagnostics-example-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(Path.Combine(fixture.Logs, "application.log"),
            $"{Stamp} [Information] Application starting.\n{Stamp} [Warning] Battery refresh failed (IOException).\n");
        await new DiagnosticExportService().ExportAsync(fixture.Logs, output, Snapshot);
        using var archive = ZipFile.OpenRead(output);
        using var metadata = JsonDocument.Parse(await ReadEntryAsync(archive, "diagnostics.json"));
        Assert.True(metadata.RootElement.GetProperty("isSynthetic").GetBoolean());
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> ReadAllEntriesAsync(ZipArchive archive)
    {
        var result = new List<string>();
        foreach (var entry in archive.Entries)
            result.Add(await ReadEntryAsync(archive, entry.FullName));
        return string.Join("\n", result);
    }

    private sealed class ExportFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(AppContext.BaseDirectory, "test-output", "diagnostic-export-" + Guid.NewGuid().ToString("N"));
        public string Logs => Path.Combine(Root, "Logs");
        public string Destination => Path.Combine(Root, "diagnostics.zip");

        public ExportFixture() => Directory.CreateDirectory(Logs);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
