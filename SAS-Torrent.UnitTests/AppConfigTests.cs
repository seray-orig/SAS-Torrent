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
    public void ResolveDownloadsFolder_Default_UsesProgramDirectory()
    {
        var config = new AppConfig();

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "downloads"),
            config.ResolveDownloadsFolder());
    }

    [Fact]
    public void ResolveDownloadsFolder_CustomPath_IsUsedAsIs()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "custom-downloads");
        var config = new AppConfig { CustomDownloadsFolderPath = customPath };

        Assert.Equal(customPath, config.ResolveDownloadsFolder());
    }

    [Fact]
    public void ResolveDownloadDirectory_Default_AddsTorrentSubfolder()
    {
        var config = new AppConfig { DownloadsFolderName = "my-downloads" };

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "my-downloads", "TorrentName"),
            config.ResolveDownloadDirectory("TorrentName"));
    }

    [Fact]
    public void ResolveDownloadDirectory_CustomPath_DoesNotAddTorrentSubfolder()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "custom-downloads");
        var config = new AppConfig { CustomDownloadsFolderPath = customPath };

        Assert.Equal(customPath, config.ResolveDownloadDirectory("TorrentName"));
    }

    [Fact]
    public void EnsureCreated_CreatesConfigFile_WithDefaults()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sas-torrent-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var config = AppConfig.EnsureCreated(directory);
            var path = AppConfig.GetConfigPath(directory);
            var json = File.ReadAllText(path);

            Assert.True(File.Exists(path));
            Assert.Equal("downloads", config.DownloadsFolderName);
            Assert.Null(config.CustomDownloadsFolderPath);
            Assert.Contains("\"downloadsFolderName\": \"downloads\"", json);
            Assert.Contains("\"customDownloadsFolderPath\": null", json);
            Assert.DoesNotContain("HasCustomDownloadsPath", json);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}
