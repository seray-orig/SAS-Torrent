using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.Config;
using SAS_Torrent.Core.IO;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public partial class MainWindow : Window
{
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/seray-orig/SAS-Torrent/releases/latest";
    private const string GitHubLatestReleaseUrl = "https://github.com/seray-orig/SAS-Torrent/releases/latest";
    private const string GitHubIssuesUrl      = "https://github.com/seray-orig/SAS-Torrent/issues";

    /// <summary>
    /// Полная текстовая версия, полученная из -p:Version (через AssemblyInformationalVersion).
    /// </summary>
    private static readonly string FullVersionString = BuildFullVersionString();

    private readonly AppConfig _config;
    private readonly ObservableCollection<TorrentQueueItem> _queue = new();

    private bool _languageEng;
    private bool _hasUpdate;

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();

        Title = $"SAS Torrent {FullVersionString}";

        try { _config = AppConfig.EnsureCreated(); }
        catch { _config = new AppConfig(); }

        DownloadFolderBox.Text = _config.ResolveDownloadsFolder();
        TorrentsListBox.ItemsSource = _queue;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Closing += (s, e) => StopAll();

        foreach (var arg in args)
        {
            if (File.Exists(arg) && arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                AddTorrentToQueue(arg);
        }

        UpdateQueuePlaceholder();

        _ = CheckForUpdatesAsync();
    }

    private static string BuildFullVersionString()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info))
                return asm.GetName().Version?.ToString() ?? "unknown";

            var plus = info.IndexOf('+');
            if (plus >= 0) info = info[..plus];
            return info.Trim();
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Числовая версия для сравнения с GitHub.
    /// Поддерживаются теги вида "v1.2.3" и "dev1.2.3". Нечисловые значения возвращают null.
    /// </summary>
    private static Version? GetComparableVersion() => TryParseLooseVersion(FullVersionString);

    private static Version? TryParseLooseVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim();

        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];

        if (s.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
            s = s[3..];
        else if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        return Version.TryParse(s, out var v) ? v : null;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private void OnLanguageClick(object? sender, RoutedEventArgs e)
    {
        _languageEng = !_languageEng;
        LanguageButton.Content = _languageEng ? "eng" : "rus";
    }

    private async void OnVersionClick(object? sender, RoutedEventArgs e)
    {
        if (!_hasUpdate)
            await CheckForUpdatesAsync();

        await OpenUrlAsync(GitHubLatestReleaseUrl);
    }

    private async void OnIssueClick(object? sender, RoutedEventArgs e)
    {
        await OpenUrlAsync(GitHubIssuesUrl);
    }

    private async Task OpenUrlAsync(string url)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Launcher != null)
            {
                await topLevel.Launcher.LaunchUriAsync(new Uri(url));
                return;
            }
        }
        catch { }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            GlobalStatusText.Text = $"Не удалось открыть ссылку: {url} ({ex.Message})";
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        var current = GetComparableVersion();
        if (current == null) return;

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SAS-Torrent");
            http.DefaultRequestHeaders.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            http.Timeout = TimeSpan.FromSeconds(10);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var json = await http.GetStringAsync(GitHubLatestReleaseApi, cts.Token);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tag_name", out var tagEl)) return;
            var tag = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var latest = TryParseLooseVersion(tag);
            if (latest == null) return;

            if (Normalize(latest) > Normalize(current))
            {
                _hasUpdate = true;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    VersionButton.Content = "Обнови клиент!";
                    VersionButton.Classes.Set("btn-green", true);
                });
            }
        }
        catch { }
    }

    private void AddTorrentToQueue(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (BencodeParser.Parse(bytes) is not BencodeDictionary root)
                throw new InvalidDataException("Неверный формат .torrent файла");

            var torrent = new TorrentInfo(root);
            var infoHashHex = Convert.ToHexString(torrent.InfoHash);

            if (_queue.Any(q => string.Equals(q.InfoHashHex, infoHashHex, StringComparison.OrdinalIgnoreCase)))
            {
                GlobalStatusText.Text = $"Торрент уже есть в очереди: {torrent.Name}";
                return;
            }

            var item = new TorrentQueueItem(
                torrent, 
                () => _config.ResolveDownloadDirectory(FileWriter.SanitizeName(torrent.Name)),
                _config,
                RemoveTorrentFromQueue
            );
            
            item.PropertyChanged += OnItemPropertyChanged;
            _queue.Add(item);
            
            GlobalStatusText.Text = $"Добавлен торрент: {torrent.Name}";
            UpdateQueuePlaceholder();
            UpdateGlobalActionState();
        }
        catch (Exception ex)
        {
            GlobalStatusText.Text = $"Ошибка добавления {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void RemoveTorrentFromQueue(TorrentQueueItem item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        _queue.Remove(item);
        UpdateQueuePlaceholder();
        UpdateGlobalActionState();
    }

    private void UpdateQueuePlaceholder()
    {
        EmptyQueuePlaceholder.IsVisible = _queue.Count == 0;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorrentQueueItem.IsDownloading))
        {
            UpdateGlobalActionState();
        }
    }

    private void UpdateGlobalActionState()
    {
        bool anyDownloading = _queue.Any(q => q.IsDownloading);
        GlobalActionButton.Content = anyDownloading ? "Остановить все" : "Начать все";
        GlobalActionButton.Classes.Set("btn-red", anyDownloading);
        GlobalActionButton.Classes.Set("btn-green", !anyDownloading);
    }

    private void OnGlobalActionClick(object? sender, RoutedEventArgs e)
    {
        bool anyDownloading = _queue.Any(q => q.IsDownloading);
        
        if (anyDownloading)
        {
            foreach (var item in _queue.Where(q => q.IsDownloading))
                item.Stop();
                
            GlobalStatusText.Text = "Все загрузки остановлены";
        }
        else
        {
            int started = 0;
            foreach (var item in _queue)
            {
                if (!item.IsDownloading && item.HasSelectedFiles)
                {
                    item.Start();
                    started++;
                }
            }
            GlobalStatusText.Text = started > 0 ? $"Запущено торрентов: {started}" : "Нет торрентов для запуска";
        }
        
        UpdateGlobalActionState();
    }

    private async void OnAddTorrentClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите торрент-файл",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Torrent Files") { Patterns = new[] { "*.torrent" } } }
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) AddTorrentToQueue(path);
        }
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для сохранения торрентов",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        _config.CustomDownloadsFolderPath = path;
        SaveConfig();
    }

    private void OnResetFolderClick(object? sender, RoutedEventArgs e)
    {
        _config.CustomDownloadsFolderPath = null;
        SaveConfig();
    }

    private void SaveConfig()
    {
        try { AppConfig.Save(_config); } catch { }
        DownloadFolderBox.Text = _config.ResolveDownloadsFolder();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasTorrents = e.DataTransfer.TryGetFiles()?.Any(f => f.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) == true;
        e.DragEffects = hasTorrents ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                AddTorrentToQueue(path);
        }
    }

    private void StopAll()
    {
        foreach (var item in _queue)
            item.Stop();
    }
}