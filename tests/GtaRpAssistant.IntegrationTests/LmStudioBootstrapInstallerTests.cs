using GtaRpAssistant.Infrastructure.Windows;

namespace GtaRpAssistant.IntegrationTests;

public sealed class LmStudioBootstrapInstallerTests
{
    [Fact]
    public void InstallerScript_MustContainExpectedTrustMarkers()
    {
        var script = System.Text.Encoding.UTF8.GetBytes("$APP_NAME = 'llmster'\nfunction Test-Checksum {}\nfunction Install-Llmster {}");
        LmStudioBootstrapInstaller.ValidateInstallerScript(script);
        Assert.Throws<InvalidDataException>(() => LmStudioBootstrapInstaller.ValidateInstallerScript("Write-Host hacked"u8));
    }

    [Fact]
    public void StartInfo_UsesArgumentListAndSelectedHome()
    {
        var info = LmStudioBootstrapInstaller.CreateStartInfo(@"E:\AI Tools\installer.ps1", @"E:\AI Tools");
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Contains(@"E:\AI Tools\installer.ps1", info.ArgumentList);
        Assert.Equal(@"E:\AI Tools", info.Environment["HOME"]);
        Assert.Equal("1", info.Environment["LMS_NO_MODIFY_PATH"]);
    }

    [Fact]
    public void InstallHome_RejectsDriveRoot()
    {
        Assert.Throws<ArgumentException>(() => LmStudioBootstrapInstaller.ValidateInstallHome(@"E:\"));
        Assert.Equal(Path.GetFullPath(@"E:\AI Tools"), LmStudioBootstrapInstaller.ValidateInstallHome(@"E:\AI Tools"));
    }
}
