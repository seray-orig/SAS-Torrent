using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly ObservableCollection<FileEntry> _fileEntries = new();
    private readonly ObservableCollection<string> _logEntries = new();

    private AppConfig _config = new();
    private TorrentInfo? _torrent;
    private string _lastTorrentPath = string.Empty;

    private TorrentDownload? _download;
    private CancellationTokenSource? _downloadCts;
    private DispatcherTimer? _progressTimer;

    private bool _isDownloading;
    private bool _stopRequested;
    private bool _lastRunCompleted;
    private int _downloadTotalPieces;

    public MainWindow()
    {
        InitializeComponent();

        _config = LoadConfig();
        DownloadFolderBox.Text = _config.ResolveDownloadsFolder();

        FilesListBox.ItemsSource = _fileEntries;
        LogListBox.ItemsSource = _logEntries;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Closing += OnWindowClosing;
        UpdateSelectionProgress();
    }

    private static AppConfig LoadConfig()
    {
        try
        {
            return AppConfig.EnsureCreated();
        }
        catch
        {
            return new AppConfig();
        }
    }

    private async void OnBrowseTorrentClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите торрент-файл",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Торрент-файлы")
                {
                    Patterns = new[] { "*.torrent" }
                }
            }
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        TorrentPathBox.Text = path;
        LoadTorrent(path);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_config);
        var saved = await dialog.ShowDialog<bool?>(this);

        if (saved == true)
            RefreshDownloadFolder();
    }

    private void RefreshDownloadFolder()
    {
        DownloadFolderBox.Text = _config.ResolveDownloadsFolder();
        _lastRunCompleted = false;
        UpdateSelectionProgress();
    }

    private void OnTorrentPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        LoadTorrent(TorrentPathBox.Text?.Trim() ?? string.Empty);
        e.Handled = true;
    }

    private void OnTorrentPathLostFocus(object? sender, RoutedEventArgs e)
    {
        var path = TorrentPathBox.Text?.Trim() ?? string.Empty;
        if (path == _lastTorrentPath)
            return;

        LoadTorrent(path);
    }

    private void LoadTorrent(string path)
    {
        _lastTorrentPath = path;

        if (string.IsNullOrWhiteSpace(path))
        {
            ClearTorrent();
            SetStatus(string.Empty);
            return;
        }

        if (!File.Exists(path))
        {
            ClearTorrent();
            SetStatus("Файл торрента не найден.");
            return;
        }

        try
        {
            var root = BencodeParser.Parse(File.ReadAllBytes(path)) as BencodeDictionary
                       ?? throw new InvalidDataException("Неверный формат торрент-файла.");

            var torrent = new TorrentInfo(root);
            _torrent = torrent;
            _lastRunCompleted = false;

            TorrentPathBox.Text = path;
            TorrentNameText.Text = torrent.Name;
            TorrentSizeText.Text = $"Размер: {SizeFormatter.Format(torrent.TotalLength)}";
            TorrentFilesText.Text = $"Файлов: {torrent.Files.Count}";

            BuildFileList(torrent);
            SetStatus(string.Empty);
            UpdateSelectionProgress();
        }
        catch (Exception ex)
        {
            ClearTorrent();
            SetStatus($"Ошибка чтения торрента: {ex.Message}");
        }
    }

    private void BuildFileList(TorrentInfo torrent)
    {
        foreach (var entry in _fileEntries)
            entry.PropertyChanged -= OnFileEntryPropertyChanged;

        _fileEntries.Clear();

        foreach (var file in torrent.Files)
        {
            file.IsSelected = true;
            var entry = new FileEntry(file);
            entry.PropertyChanged += OnFileEntryPropertyChanged;
            _fileEntries.Add(entry);
        }
    }

    private void ClearTorrent()
    {
        foreach (var entry in _fileEntries)
            entry.PropertyChanged -= OnFileEntryPropertyChanged;

        _fileEntries.Clear();
        _torrent = null;
        _lastRunCompleted = false;

        TorrentNameText.Text = "Торрент не выбран";
        TorrentSizeText.Text = string.Empty;
        TorrentFilesText.Text = string.Empty;
        DownloadButton.IsEnabled = false;

        UpdateSelectionProgress();
    }

    private void OnFileEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileEntry.IsSelected))
            return;

        _lastRunCompleted = false;
        UpdateSelectionProgress();
    }

    private void UpdateSelectionProgress()
    {
        if (_torrent == null)
        {
            SelectionProgressText.Text = "0 / 0";
            DownloadButton.IsEnabled = false;
            return;
        }

        var total = TorrentSelection.CountSelectedPieces(_torrent, _torrent.Files);
        var current = _lastRunCompleted
            ? total
            : TorrentSelection.CountSavedSelectedPieces(
                _torrent,
                GetEffectiveDownloadDirectory(_torrent),
                _torrent.Files);

        SelectionProgressText.Text = $"{current} / {total}";
        DownloadButton.IsEnabled = !_isDownloading && total > 0;
    }

    private string GetEffectiveDownloadDirectory(TorrentInfo torrent)
    {
        return _config.ResolveDownloadDirectory(FileWriter.SanitizeName(torrent.Name));
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_isDownloading || _torrent == null)
            return;

        var total = TorrentSelection.CountSelectedPieces(_torrent, _torrent.Files);
        if (total == 0)
        {
            SetStatus("Выберите хотя бы один файл.");
            return;
        }

        var downloadDir = GetEffectiveDownloadDirectory(_torrent);

        SetStatus(string.Empty);
        _isDownloading = true;
        _stopRequested = false;
        _lastRunCompleted = false;
        _downloadTotalPieces = total;

        _logEntries.Clear();

        TorrentPathBox.IsEnabled = false;
        DownloadFolderBox.IsEnabled = false;
        DownloadButton.IsEnabled = false;

        DownloadTorrentNameText.Text = _torrent.Name;
        DownloadProgressText.Text = $"0 / {total}";
        DownloadProgressBar.Minimum = 0;
        DownloadProgressBar.Maximum = total;
        DownloadProgressBar.Value = 0;
        DownloadStatusText.Text = "Активных соединений: 0";
        StopButton.IsEnabled = true;
        StopButton.Content = "Stop";

        SelectionView.IsVisible = false;
        DownloadView.IsVisible = true;

        _downloadCts = new CancellationTokenSource();
        _download = new TorrentDownload(_torrent, downloadDir, new UiTorrentLogger(AppendLog));
        StartProgressTimer();

        var completed = false;
        var canceled = false;
        string? error = null;

        try
        {
            completed = await Task.Run(
                () => _download.RunAsync(_downloadCts.Token),
                _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            error = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            canceled |= _downloadCts?.IsCancellationRequested == true;

            StopProgressTimer();
            _downloadCts?.Dispose();
            _downloadCts = null;
            _download = null;
            _isDownloading = false;
            _lastRunCompleted = completed;

            DownloadView.IsVisible = false;
            SelectionView.IsVisible = true;

            TorrentPathBox.IsEnabled = true;
            DownloadFolderBox.IsEnabled = true;
            StopButton.IsEnabled = true;
            StopButton.Content = "Stop";

            if (completed)
                SetStatus("Готово.");
            else if (canceled)
                SetStatus("Загрузка остановлена.");
            else if (error != null)
                SetStatus(error);
            else
                SetStatus("Загрузка прервана.");

            UpdateSelectionProgress();
        }
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        if (!_isDownloading)
            return;

        StopButton.IsEnabled = false;
        StopButton.Content = "Остановка...";
        _stopRequested = true;
        DownloadStatusText.Text = "Останавливаем загрузку...";
        _download?.Stop();
    }

    private void StartProgressTimer()
    {
        StopProgressTimer();

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _progressTimer.Tick += OnProgressTick;
        _progressTimer.Start();
    }

    private void StopProgressTimer()
    {
        if (_progressTimer == null)
            return;

        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTick;
        _progressTimer = null;
    }

    private void OnProgressTick(object? sender, EventArgs e)
    {
        var download = _download;
        if (download == null)
            return;

        var total = download.TotalPieces > 0 ? download.TotalPieces : _downloadTotalPieces;
        if (total > 0)
        {
            _downloadTotalPieces = total;
            DownloadProgressBar.Maximum = total;
        }

        var completed = download.CompletedPieces;
        DownloadProgressText.Text = $"{completed} / {_downloadTotalPieces}";
        DownloadProgressBar.Value = Math.Clamp(completed, 0, Math.Max(1, _downloadTotalPieces));

        if (!_stopRequested)
            DownloadStatusText.Text = $"Активных соединений: {download.ActivePeerCount}";
    }

    private void AppendLog(string text)
    {
        _logEntries.Add(text);

        while (_logEntries.Count > 1000)
            _logEntries.RemoveAt(0);

        if (_logEntries.Count > 0)
            LogListBox.ScrollIntoView(_logEntries[^1]);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !_isDownloading && GetTorrentPathFromDrop(e) != null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_isDownloading)
            return;

        var path = GetTorrentPathFromDrop(e);
        if (path == null)
            return;

        TorrentPathBox.Text = path;
        LoadTorrent(path);
    }

    private static string? GetTorrentPathFromDrop(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files == null)
            return null;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path) &&
                path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _progressTimer?.Stop();
        _downloadCts?.Cancel();
        _download?.Stop();
        _download?.FlushState();
    }
}
