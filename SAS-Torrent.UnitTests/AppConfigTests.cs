using SAS_Torrent.Core.Config;

namespace SAS_Torrent.UnitTests;

public class AppConfigTests
{
    [Fact]
    public void GetConfigPath_Default_UsesAppDataDirectory()
    {
        var path = AppConfig.GetConfigPath();

        Assert.Equal(AppConfig.GetAppDataDirectory(), Path.GetDirectoryName(path));
        Assert.Equal("appsettings.json", Path.GetFileName(path));
    }

    [Fact]
    public void ResolveDownloadsFolder_Default_UsesAppDataDirectory()
    {
        var config = new AppConfig();

        Assert.Equal(
            Path.Combine(AppConfig.GetAppDataDirectory(), "downloads"),
            config.ResolveDownloadsFolder());
    }

    [Fact]
    public void EnsureCreated_CreatesConfigFile_WithDefaultDownloadsFolder()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sas-torrent-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var config = AppConfig.EnsureCreated(directory);
            var path = AppConfig.GetConfigPath(directory);

            Assert.True(File.Exists(path));
            Assert.Equal("downloads", config.DownloadsFolderName);
            Assert.Contains("\"downloadsFolderName\": \"downloads\"", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
