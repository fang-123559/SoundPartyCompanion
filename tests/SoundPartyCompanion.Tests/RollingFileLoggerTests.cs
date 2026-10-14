using SoundPartyCompanion.Core.Diagnostics;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class RollingFileLoggerTests
{
    [Fact]
    public void Write_CreatesUtf8LogEntry()
    {
        var testDirectory = CreateTestDirectory();

        try
        {
            var logger = new RollingFileLogger(testDirectory);
            logger.Write(AppLogLevel.Information, "阶段 0 smoke test");

            var content = File.ReadAllText(logger.LogFilePath);
            Assert.Contains("[Information]", content);
            Assert.Contains("阶段 0 smoke test", content);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Write_RotatesWhenMaximumSizeIsExceeded()
    {
        var testDirectory = CreateTestDirectory();

        try
        {
            var logger = new RollingFileLogger(testDirectory, maximumBytes: 64, retainedFiles: 2);
            logger.Write(AppLogLevel.Information, new string('A', 80));
            logger.Write(AppLogLevel.Information, new string('B', 80));

            Assert.True(File.Exists(logger.LogFilePath));
            Assert.True(File.Exists($"{logger.LogFilePath}.1"));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-output", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Write_RedactsPrivatePathsAndSensitiveFields()
    {
        var testDirectory = CreateTestDirectory();
        try
        {
            var logger = new RollingFileLogger(testDirectory);
            logger.Write(AppLogLevel.Warning, @"Read failed at E:\SyntheticUser\private\sample.txt");
            logger.Write(AppLogLevel.Warning, "token=synthetic-only-value");
            logger.Write(AppLogLevel.Warning, "artist=synthetic artist");
            var content = File.ReadAllText(logger.LogFilePath);
            Assert.DoesNotContain("SyntheticUser", content);
            Assert.DoesNotContain("sample.txt", content);
            Assert.DoesNotContain("synthetic-only-value", content);
            Assert.DoesNotContain("synthetic artist", content);
            Assert.Contains("[redacted]", content);
        }
        finally { Directory.Delete(testDirectory, recursive: true); }
    }

    [Fact]
    public void Write_DoesNotPersistExceptionMessagesStackPathsOrData()
    {
        var testDirectory = CreateTestDirectory();
        try
        {
            var logger = new RollingFileLogger(testDirectory);
            var exception = new IOException(@"private media text at E:\SyntheticUser\sample.txt");
            exception.Data["private"] = "synthetic private data";
            logger.Write(AppLogLevel.Error, "Unhandled UI exception.", exception);
            var content = File.ReadAllText(logger.LogFilePath);
            Assert.Contains("ExceptionType=IOException; HResult=0x", content);
            Assert.DoesNotContain("private media", content);
            Assert.DoesNotContain("SyntheticUser", content);
            Assert.DoesNotContain("synthetic private data", content);
            Assert.Contains("ExceptionType=IOException", LogPrivacyFilter.FilterForExport(content));
        }
        finally { Directory.Delete(testDirectory, recursive: true); }
    }

    [Fact]
    public void Write_UnavailableLogFileDoesNotThrowIntoTheApplication()
    {
        var testDirectory = CreateTestDirectory();
        try
        {
            var logger = new RollingFileLogger(testDirectory);
            Directory.CreateDirectory(logger.LogFilePath);
            var failure = Record.Exception(() => logger.Write(AppLogLevel.Warning, "Diagnostic export completed."));
            Assert.Null(failure);
        }
        finally { Directory.Delete(testDirectory, recursive: true); }
    }
}
