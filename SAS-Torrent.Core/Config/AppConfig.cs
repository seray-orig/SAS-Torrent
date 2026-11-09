using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAS_Torrent.Core.Config;

public sealed class AppConfig
{
    public const string DefaultUserAgent = "qBittorrent/4.6.4";
    public const string DefaultPeerIdPrefix = "-qB4640-";

    public static readonly string[] DefaultDhtBootstrapHosts =
    {
        "dht.transmissionbt.com",
        "router.bittorrent.com",
        "router.utorrent.com",
        "dht.aelitis.com",
        "dht.libtorrent.org"
    };

    private const string ConfigFileName = "appsettings.json";
    private const string AppDirectoryName = "SAS-Torrent";
    private const string DefaultDownloadsFolderName = "SAS-downloads";

    [JsonPropertyName("customDownloadsFolderPath")]
    public string? CustomDownloadsFolderPath { get; set; }

    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = DefaultUserAgent;

    [JsonPropertyName("peerIdPrefix")]
    public string PeerIdPrefix { get; set; } = DefaultPeerIdPrefix;

    [JsonPropertyName("dhtBootstrapHosts")]
    public List<string> DhtBootstrapHosts { get; set; } = DefaultDhtBootstrapHosts.ToList();

    [JsonIgnore]
    public bool HasCustomDownloadsPath => !string.IsNullOrWhiteSpace(CustomDownloadsFolderPath);

    [JsonIgnore]
    public string ResolvedUserAgent =>
        string.IsNullOrWhiteSpace(UserAgent) ? DefaultUserAgent : UserAgent.Trim();

    [JsonIgnore]
    public string ResolvedPeerIdPrefix =>
        IsValidPeerIdPrefix(PeerIdPrefix) ? PeerIdPrefix : DefaultPeerIdPrefix;

    [JsonIgnore]
    public IReadOnlyList<string> ResolvedDhtBootstrapHosts
    {
        get
        {
            var hosts = (DhtBootstrapHosts ?? new List<string>())
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return hosts.Count > 0 ? hosts : DefaultDhtBootstrapHosts;
        }
    }

    public string CreatePeerId()
    {
        return ResolvedPeerIdPrefix
            + RandomNumberGenerator.GetInt32(100000000).ToString("D8")
            + RandomNumberGenerator.GetInt32(10000).ToString("D4");
    }

    public void SetClientIdentity(string userAgent, string peerIdPrefix, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            throw new ArgumentException("User-Agent не может быть пустым.", nameof(userAgent));

        if (userAgent.Any(character => character is '\r' or '\n'))
            throw new ArgumentException("User-Agent не может содержать перевод строки.", nameof(userAgent));

        if (!IsValidPeerIdPrefix(peerIdPrefix))
            throw new ArgumentException(
                "Peer ID prefix должен состоять из 8 печатных ASCII-символов, например -qB4640-.",
                nameof(peerIdPrefix));

        UserAgent = userAgent.Trim();
        PeerIdPrefix = peerIdPrefix;
        Save(this, baseDirectory);
    }

    public static bool IsValidPeerIdPrefix(string? prefix)
    {
        if (prefix is null || prefix.Length != 8)
            return false;

        return prefix.All(character => character is >= ' ' and <= '~');
    }

    public static string GetAppDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            root = string.IsNullOrWhiteSpace(home)
                ? AppContext.BaseDirectory
                : Path.Combine(home, ".local", "share");
        }

        return Path.Combine(root, AppDirectoryName);
    }

    public static string GetConfigPath(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? GetAppDataDirectory();
        return Path.Combine(dir, ConfigFileName);
    }

    public static AppConfig Load(string? baseDirectory = null)
    {
        var path = GetConfigPath(baseDirectory);
        if (!File.Exists(path))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfig);
            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static AppConfig EnsureCreated(string? baseDirectory = null)
    {
        var path = GetConfigPath(baseDirectory);

        if (!File.Exists(path))
        {
            var created = new AppConfig();
            Save(created, baseDirectory);
            return created;
        }

        var config = Load(baseDirectory);

        try
        {
            var json = File.ReadAllText(path);
            if (json.Contains("\"downloadsFolderName\"", StringComparison.Ordinal) ||
                !json.Contains("\"userAgent\"", StringComparison.Ordinal) ||
                !json.Contains("\"peerIdPrefix\"", StringComparison.Ordinal) ||
                !json.Contains("\"dhtBootstrapHosts\"", StringComparison.Ordinal))
                Save(config, baseDirectory);
        }
        catch
        {
        }

        return config;
    }

    public static void Save(AppConfig config, string? baseDirectory = null)
    {
        var path = GetConfigPath(baseDirectory);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, AppConfigJsonContext.Default.AppConfig);
        File.WriteAllText(path, json);
    }

    public string GetDefaultDownloadsFolder(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;

        return Path.Combine(dir, DefaultDownloadsFolderName);
    }

    public string ResolveDownloadsFolder(string? baseDirectory = null)
    {
        if (HasCustomDownloadsPath)
        {
            var path = CustomDownloadsFolderPath!.Trim();
            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(baseDirectory ?? AppContext.BaseDirectory, path);
        }

        return GetDefaultDownloadsFolder(baseDirectory);
    }

    public string ResolveDownloadDirectory(string torrentFolderName, string? baseDirectory = null)
    {
        return Path.Combine(ResolveDownloadsFolder(baseDirectory), torrentFolderName);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}
