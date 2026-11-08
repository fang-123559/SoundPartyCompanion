using System.Xml.Linq;
using Xunit;

namespace SoundPartyCompanion.Tests;

// Source contract checks complement ISCC compilation, not real installation tests.
public sealed class InstallerContractTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SoundPartyCompanion.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Project root not found.");
        }
    }

    private static string Installer => File.ReadAllText(Path.Combine(Root, "installer", "SoundPartyCompanion.iss"));

    [Fact]
    public void AppAndInstallerHaveConsistentReleaseVersion()
    {
        var project = XDocument.Load(Path.Combine(Root, "src", "SoundPartyCompanion.App", "SoundPartyCompanion.App.csproj"));
        var version = Assert.Single(project.Descendants("Version")).Value;
        Assert.Contains($"#define MyAppVersion \"{version}\"", Installer);
        Assert.Equal(version + ".0", Assert.Single(project.Descendants("FileVersion")).Value);
        Assert.Equal(version, typeof(App.App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion);
    }

    [Fact]
    public void PayloadContainsOnlyExecutableAndRequiredNotices()
    {
        var files = Installer.Split("[Files]", StringSplitOptions.None)[1].Split("[Icons]", StringSplitOptions.None)[0];
        Assert.Equal(10, files.Split('\n').Count(line => line.StartsWith("Source:", StringComparison.Ordinal)));
        Assert.Contains("win-x64\\{#MyAppExeName}", files);
        Assert.Contains("win-x64\\LICENSE", files);
        Assert.Contains("windowsdesktop-THIRD-PARTY-NOTICES.txt", files);
        Assert.Contains("windows-sdk-license.rtf", files);
        Assert.Contains("cswinrt-LICENSE.txt", files);
        Assert.DoesNotContain("recursesubdirs", files);
        Assert.DoesNotContain("\\*", files);
    }

    [Fact]
    public void InstallerUsesCurrentUserWindows11AndAppIdentity()
    {
        Assert.Contains("PrivilegesRequired=lowest", Installer);
        Assert.Contains("MinVersion=10.0.22000", Installer);
        Assert.Contains("ArchitecturesAllowed=x64os", Installer);
        Assert.Contains("SetupIconFile=..\\src\\SoundPartyCompanion.App\\Assets\\app.ico", Installer);
        Assert.Contains("AppMutex=Local\\SoundPartyCompanion.App.v1.Mutex", Installer);
        Assert.Contains("RestartApplications=no", Installer);
    }

    [Fact]
    public void StartupMatchesApplicationAndPreservesExistingPreference()
    {
        Assert.Contains(" --startup\"; Check: ShouldRegisterStartup", Installer);
        Assert.DoesNotContain("[Tasks]", Installer);
        Assert.DoesNotContain("uninsdeletevalue", Installer);
        Assert.Contains("Result := RegValueExists(HKCU, RunKey, RunValue)", Installer);
        Assert.Contains("CompareText(Trim(Command)", Installer);
        Assert.Contains("RegDeleteValue(HKCU, RunKey, RunValue)", Installer);
    }

    [Fact]
    public void UninstallDefaultsToPreservingDataAndNeverRecursivelyDeletes()
    {
        Assert.Contains("if UninstallSilent then Exit", Installer);
        Assert.Contains("MB_YESNO or MB_DEFBUTTON2, IDNO", Installer);
        Assert.Contains("FileAttributeReparsePoint", Installer);
        Assert.Contains("FindFirst(Path, FileInfo)", Installer);
        Assert.Contains("FindClose(FileInfo)", Installer);
        Assert.DoesNotContain("GetFileAttributesW@", Installer);
        Assert.Contains("settings.json.tmp", Installer);
        Assert.Contains("speaker-selection.json.tmp", Installer);
        Assert.Contains("for Index := 1 to 3", Installer);
        Assert.DoesNotContain("DelTree", Installer);
        Assert.DoesNotContain("[UninstallDelete]", Installer);
    }

    [Fact]
    public void PackagingRequiresTestsVersionAndOfficialCompilerSignature()
    {
        var packaging = File.ReadAllText(Path.Combine(Root, "scripts", "package.ps1"));
        var bootstrap = File.ReadAllText(Path.Combine(Root, "scripts", "install-inno.ps1"));
        Assert.Contains("-Task All", packaging);
        Assert.Contains("$counters.passed -ne [int]$counters.total", packaging);
        Assert.Contains("$appMetadata.ProductVersion -ne $version", packaging);
        Assert.Contains(".sha256", packaging);
        Assert.Contains("$signature.Status -ne 'Valid'", bootstrap);
        Assert.Contains("CN=Pyrsys", bootstrap);
        Assert.Contains("/PORTABLE=1", bootstrap);
        Assert.Contains("-WindowStyle Hidden", bootstrap);
    }
}
