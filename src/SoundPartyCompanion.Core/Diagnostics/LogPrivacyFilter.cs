using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SoundPartyCompanion.Core.Diagnostics;

public static class LogPrivacyFilter
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex SensitiveText = new(
        """(?:[A-Z]:[\\/]|\\\\|/(?:home|Users|tmp|var)/)[^\r\n]*|\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b|\b(?:Dev_)?[0-9A-F]{12}\b|\bbt-[0-9A-F]{32}\b|\b[0-9A-F]{8}(?:-[0-9A-F]{4}){3}-[0-9A-F]{12}\b|\b(?:password|pwd|secret|token|api[_ -]?key|authorization|username|user[_ -]?name|title|artist|track[_ -]?title|歌名|歌手)["']?\s*[:=][^\r\n]*|\bBearer\s+\S+|\b[\w.+-]+@[\w.-]+\.[A-Z]{2,}\b|\b(?:\d{1,3}\.){3}\d{1,3}\b""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex LogEntry = new(
        @"\A(?<time>[0-9T:Z.+-]{20,40}) \[(?<level>Trace|Debug|Information|Warning|Error|Critical)\] (?<message>.*)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    // Only application-authored event templates cross the export boundary.
    // Unknown free text and legacy exception continuations never leave the log directory.
    private static readonly Regex SafeEvent = new(
        @"\A(?:Application starting\.|Application exiting with code -?\d{1,10}\.|(?:Unhandled UI|Unhandled application-domain|Unobserved task) exception\.(?: ExceptionType=[A-Za-z0-9_]{1,128}; HResult=0x[0-9A-F]{8})?|(?:Connection refresh|Battery refresh|Bluetooth discovery|Audio endpoint snapshot|Audio endpoint control|Default audio output probe|Media session snapshot|Media session control|Diagnostic export) failed \([A-Za-z0-9_]{1,128}\)\.|Classic Bluetooth radio enumeration was skipped \([A-Za-z0-9_]{1,128}\)\.|Startup state could not be (?:read|changed) \([A-Za-z0-9_]{1,128}\)\.|Local (?:settings|speaker selection) could not be (?:loaded|saved) \([A-Za-z0-9_]{1,128}\)\.|Default audio output switch needs user action \([A-Za-z0-9_]{1,128}\)\.|Diagnostic export completed\.)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    private const string UtcTimestamp = @"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z";
    private static readonly Regex ConnectionProbeEvent = new(
        @"\A(?:Speaker discovery observed\. Connected=[01] DurationMs=[0-9]{1,10}|Speaker connection probes completed\. BatteryMs=[0-9]{1,10} DefaultOutputMs=[0-9]{1,10}|Audio endpoint inventory item unavailable\.)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex EndpointDiagnosticEvent = new(
        $@"\A(?:Audio endpoint inventory failed\.|Audio endpoint decision\. Session=[a-f0-9]{{8}} Endpoint=[0-9]{{1,6}} State=[0-9]{{1,10}} ExpectedContainer=[0-9]{{1,6}} ActualContainer=[0-9]{{1,6}} Reason=(?:Inactive|ContainerMismatch|NameMissing|NameMismatch|ReadFailed|Selected|SelectionExcluded|HandsFree|NotSelected) Default=[01]|Audio endpoint scan\. Session=[a-f0-9]{{8}} ActiveCount=[0-9]{{1,6}} Candidates=[0-9]{{1,6}} Matches=[0-9]{{1,6}} Selected=[01]|Audio endpoint event\. Session=[a-f0-9]{{8}} Endpoint=[0-9]{{1,6}} Kind=(?:InitialRender|State|Added|Removed|ContainerChanged|NameChanged|DefaultConsole|DefaultMultimedia|DefaultCommunications) State=[0-9]{{1,10}} EventUtc={UtcTimestamp})\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex AudioProbeEvent = new(
        $@"\AAudio probe completed\. Operation=(?:Snapshot|DefaultOutput) StartedUtc={UtcTimestamp} DurationMs=[0-9]{{1,10}} EndpointMs=[0-9]{{1,10}} ControlsMs=[0-9]{{1,10}} IdentityReads=[0-9]{{1,6}} Outcome=(?:Offline|NoDefault|Speaker|Other|NoEndpoint|Ready|ReadFailed|Failed|Canceled)\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    private static readonly Regex SpeakerTimingEvent = new(
        $@"\A(?:Speaker endpoint activation watch (?:started|failed)\.|Audio endpoint activation lookup failed\.|Speaker playback endpoint became active\. EventUtc={UtcTimestamp}|Speaker volume first read\. ReadUtc={UtcTimestamp} DisplayUtc={UtcTimestamp}|Windows default playback changed\. EventUtc={UtcTimestamp} Role=(?:Console|Multimedia|Communications) Target=(?:Speaker|Other|None|Unknown))\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);

    public static string RedactText(string text, string? userName = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bounded = text.Length > 8192 ? text[..8192] + " [truncated]" : text;
        try
        {
            var redacted = SensitiveText.Replace(bounded, "[redacted]");
            var localName = userName ?? Environment.UserName;
            if (!string.IsNullOrWhiteSpace(localName))
            {
                redacted = redacted.Replace(localName, "[user]", StringComparison.OrdinalIgnoreCase);
            }
            return redacted;
        }
        catch (RegexMatchTimeoutException)
        {
            return "[redacted: privacy filter timeout]";
        }
    }

    public static string FilterForExport(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 8192) continue;
            try
            {
                var entry = LogEntry.Match(line);
                if (!entry.Success || !DateTimeOffset.TryParse(entry.Groups["time"].Value,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) continue;

                var message = RedactText(entry.Groups["message"].Value);
                var safeMessage = SafeEvent.IsMatch(message) || SpeakerTimingEvent.IsMatch(message) || AudioProbeEvent.IsMatch(message) || EndpointDiagnosticEvent.IsMatch(message) || ConnectionProbeEvent.IsMatch(message)
                    ? message
                    : "[omitted: non-allowlisted event]";
                result.Append(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                    .Append(" [").Append(entry.Groups["level"].Value).Append("] ")
                    .AppendLine(safeMessage);
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail closed rather than returning a potentially sensitive raw line.
            }
        }
        return result.ToString();
    }
}
