using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAS_Torrent.Core.Config;

public sealed class AppConfig
{
    private const string ConfigFileName = "appsettings.json";
    private const string AppDirectoryName = "SAS-Torrent";
    private const string DefaultDownloadsFolderName = "downloads";

    [JsonPropertyName("downloadsFolderName")]
    public string? DownloadsFolderName { get; set; } = DefaultDownloadsFolderName;

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
        var config = Load(baseDirectory);
        var path = GetConfigPath(baseDirectory);

        if (!File.Exists(path))
            Save(config, baseDirectory);

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

    public string ResolveDownloadsFolder(string? baseDirectory = null)
    {
        var name = string.IsNullOrWhiteSpace(DownloadsFolderName) ? DefaultDownloadsFolderName : DownloadsFolderName;
        var dir = baseDirectory ?? GetAppDataDirectory();

        if (Path.IsPathRooted(name))
            return name;

        return Path.Combine(dir, name);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class AppConfigJsonContext : JsonSerializerContext
{
}
