using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.Config;
using SAS_Torrent.Core.IO;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly ObservableCollection<TorrentQueueItem> _queue = new();

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        
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