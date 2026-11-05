using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAS_Torrent.Core.Config;

public sealed class AppConfig
{
    private const string ConfigFileName = "appsettings.json";
    private const string AppDirectoryName = "SAS-Torrent";
    private const string DefaultDownloadsFolderName = "SAS-downloads";

    [JsonPropertyName("customDownloadsFolderPath")]
    public string? CustomDownloadsFolderPath { get; set; }

    [JsonIgnore]
    public bool HasCustomDownloadsPath => !string.IsNullOrWhiteSpace(CustomDownloadsFolderPath);

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
            if (json.Contains("\"downloadsFolderName\"", StringComparison.Ordinal))
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
