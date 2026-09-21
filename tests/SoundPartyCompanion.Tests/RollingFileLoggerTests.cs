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
}
