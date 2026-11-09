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
            Path.Combine(AppContext.BaseDirectory, "SAS-downloads"),
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
        var config = new AppConfig();

        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "SAS-downloads", "TorrentName"),
            config.ResolveDownloadDirectory("TorrentName"));
    }

    [Fact]
    public void ResolveDownloadDirectory_CustomPath_AddsTorrentSubfolder()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "custom-downloads");
        var config = new AppConfig { CustomDownloadsFolderPath = customPath };

        Assert.Equal(
            Path.Combine(customPath, "TorrentName"),
            config.ResolveDownloadDirectory("TorrentName"));
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
            Assert.Null(config.CustomDownloadsFolderPath);
            Assert.Equal(AppConfig.DefaultUserAgent, config.UserAgent);
            Assert.Equal(AppConfig.DefaultPeerIdPrefix, config.PeerIdPrefix);
            Assert.Equal(AppConfig.DefaultDhtBootstrapHosts, config.DhtBootstrapHosts);
            Assert.Contains("\"customDownloadsFolderPath\": null", json);
            Assert.Contains("\"userAgent\": \"qBittorrent/4.6.4\"", json);
            Assert.Contains("\"peerIdPrefix\": \"-qB4640-\"", json);
            Assert.Contains("\"dhtBootstrapHosts\"", json);
            Assert.DoesNotContain("downloadsFolderName", json);
            Assert.DoesNotContain("HasCustomDownloadsPath", json);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void EnsureCreated_RemovesLegacyDownloadsFolderName()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sas-torrent-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                AppConfig.GetConfigPath(directory),
                "{\n  \"downloadsFolderName\": \"downloads\",\n  \"customDownloadsFolderPath\": null\n}");

            var config = AppConfig.EnsureCreated(directory);
            var json = File.ReadAllText(AppConfig.GetConfigPath(directory));

            Assert.Null(config.CustomDownloadsFolderPath);
            Assert.DoesNotContain("downloadsFolderName", json);
            Assert.Equal(AppConfig.DefaultUserAgent, config.UserAgent);
            Assert.Equal(AppConfig.DefaultPeerIdPrefix, config.PeerIdPrefix);
            Assert.Contains("\"userAgent\"", json);
            Assert.Contains("\"peerIdPrefix\"", json);
            Assert.Contains("\"dhtBootstrapHosts\"", json);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void CreatePeerId_UsesConfiguredPrefix()
    {
        var config = new AppConfig { PeerIdPrefix = "-XX1234-" };

        var peerId = config.CreatePeerId();

        Assert.Equal(20, peerId.Length);
        Assert.StartsWith("-XX1234-", peerId);
    }

    [Fact]
    public void ResolvedPeerIdPrefix_FallsBackToDefault_WhenInvalid()
    {
        var config = new AppConfig { PeerIdPrefix = "broken" };

        Assert.Equal(AppConfig.DefaultPeerIdPrefix, config.ResolvedPeerIdPrefix);
    }

    [Fact]
    public void ResolvedDhtBootstrapHosts_UsesDefaults_WhenListIsEmpty()
    {
        var config = new AppConfig { DhtBootstrapHosts = new List<string>() };

        Assert.Equal(AppConfig.DefaultDhtBootstrapHosts, config.ResolvedDhtBootstrapHosts);
    }

    [Fact]
    public void SetClientIdentity_UpdatesAndPersistsConfig()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sas-torrent-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var config = new AppConfig();

            config.SetClientIdentity("MyClient/2.0", "-MC2000-", directory);

            Assert.Equal("MyClient/2.0", config.UserAgent);
            Assert.Equal("-MC2000-", config.PeerIdPrefix);

            var persisted = AppConfig.Load(directory);
            Assert.Equal("MyClient/2.0", persisted.UserAgent);
            Assert.Equal("-MC2000-", persisted.PeerIdPrefix);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("-ABC-")]
    [InlineData("-ABCDEF")]
    [InlineData("-ABCDEFGH")]
    [InlineData("-AB\rCDE-")]
    public void SetClientIdentity_RejectsInvalidPeerIdPrefix(string prefix)
    {
        var config = new AppConfig();

        Assert.Throws<ArgumentException>(() => config.SetClientIdentity("MyClient/1.0", prefix));
    }

    [Fact]
    public void SetClientIdentity_RejectsUserAgentWithNewLine()
    {
        var config = new AppConfig();

        Assert.Throws<ArgumentException>(() => config.SetClientIdentity("Bad\r\nAgent", "-MC2000-"));
    }
}
