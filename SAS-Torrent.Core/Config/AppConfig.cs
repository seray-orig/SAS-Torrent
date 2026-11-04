using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAS_Torrent.Core.Config;

public sealed class AppConfig
{
    private const string ConfigFileName = "appsettings.json";

    [JsonPropertyName("downloadsFolderName")]
    public string? DownloadsFolderName { get; set; }

    public static string GetConfigPath(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
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
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config, string? baseDirectory = null)
    {
        var path = GetConfigPath(baseDirectory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(path, json);
    }

    public string ResolveDownloadsFolder(string? baseDirectory = null)
    {
        var name = string.IsNullOrWhiteSpace(DownloadsFolderName) ? "downloads" : DownloadsFolderName;
        var dir = baseDirectory ?? AppContext.BaseDirectory;

        if (Path.IsPathRooted(name))
            return name;

        return Path.Combine(dir, name);
    }
}
