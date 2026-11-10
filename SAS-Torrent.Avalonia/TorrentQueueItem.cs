using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using SAS_Torrent.Avalonia.Localization;
using SAS_Torrent.Core.Config;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public sealed class TorrentQueueItem : INotifyPropertyChanged
{
    private enum TorrentStatusKind
    {
        None,
        Connecting,
        ActivePeers,
        Completed,
        Stopped,
        Error
    }

    private static readonly TimeSpan SpeedWindowDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EtaPublishInterval = TimeSpan.FromSeconds(10);

    private TorrentDownload? _download;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _timer;

    private readonly Queue<(DateTime Timestamp, long Bytes)> _speedSamples = new();
    private readonly List<double> _etaSamples = new();
    private long _baselineBytes;
    private bool _metricsInitialized;
    private double _smoothedSpeed;
    private DateTime _lastEtaPublishTime;
    private TimeSpan? _publishedEta;

    private readonly ILanguageService _language;
    private readonly int _totalPieces;
    private TorrentStatusKind _statusKind;
    private int _statusActivePeers;
    private int _statusCompletedPieces;
    private int _statusTotalPieces;
    private string _statusError = string.Empty;

    private bool _isDownloading;
    private bool _isExpanded;
    private string _selectedFilesText = string.Empty;
    private string _speedText = string.Empty;
    private string _etaText = string.Empty;
    private int _progressValue;
    private int _progressMax = 1;
    private readonly Func<string> _getDownloadDirectory;
    private readonly AppConfig _config;

    public TorrentInfo Torrent { get; }
    public string InfoHashHex { get; }
    public string Name => Torrent.Name;
    public string SizeText => SizeFormatter.Format(Torrent.TotalLength, _language);
    public string TotalPiecesText => _language.GetString("Torrent.PiecesCountFormat", _totalPieces);
    public ObservableCollection<FileEntry> Files { get; } = new();

    public RelayCommand ToggleDownloadCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (_isDownloading == value) return;
            _isDownloading = value;
            OnPropertyChanged();
            ToggleDownloadCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string ButtonText => IsDownloading
        ? _language.GetString("Torrent.ButtonStop")
        : _language.GetString("Torrent.ButtonDownload");

    public string SelectedFilesText
    {
        get => _selectedFilesText;
        private set { _selectedFilesText = value; OnPropertyChanged(); }
    }

    public string Status => FormatStatus();

    public bool HasStatus => _statusKind != TorrentStatusKind.None;

    public string SpeedText
    {
        get => _speedText;
        private set { _speedText = value; OnPropertyChanged(); }
    }

    public string EtaText
    {
        get => _etaText;
        private set { _etaText = value; OnPropertyChanged(); }
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set { _progressValue = value; OnPropertyChanged(); }
    }

    public int ProgressMax
    {
        get => _progressMax;
        private set { _progressMax = value; OnPropertyChanged(); }
    }

    public bool HasSelectedFiles => Files.Any(f => f.IsSelected);

    public TorrentQueueItem(
        TorrentInfo torrent,
        Func<string> getDownloadDirectory,
        AppConfig config,
        ILanguageService language,
        Action<TorrentQueueItem> onRemove)
    {
        Torrent = torrent;
        InfoHashHex = Convert.ToHexString(torrent.InfoHash);
        _getDownloadDirectory = getDownloadDirectory;
        _config = config;
        _language = language;

        _totalPieces = TorrentSelection.GetPieceCount(torrent);

        ToggleDownloadCommand = new RelayCommand(ToggleDownload);
        RemoveCommand = new RelayCommand(() =>
        {
            Stop();
            onRemove(this);
        });

        long offset = 0;
        var pieceLength = torrent.PieceLength;

        foreach (var file in torrent.Files)
        {
            file.IsSelected = true;
            int filePieces = 0;
            if (file.Length > 0 && pieceLength > 0)
            {
                var firstPiece = (int)(offset / pieceLength);
                var lastPiece = (int)((offset + file.Length - 1) / pieceLength);
                filePieces = lastPiece - firstPiece + 1;
            }
            offset += file.Length;

            var entry = new FileEntry(file, filePieces, _language);
            entry.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FileEntry.IsSelected))
                    UpdateSelectionInfo();
            };
            Files.Add(entry);
        }

        SpeedText = FormatSpeedText(_smoothedSpeed);
        EtaText = FormatIdleEtaText();
        UpdateSelectionInfo();
    }

    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(TotalPiecesText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));

        SpeedText = FormatSpeedText(_smoothedSpeed);
        RefreshEtaText();
        UpdateSelectionInfo();

        foreach (var entry in Files)
            entry.OnLanguageChanged();
    }

    private void UpdateSelectionInfo()
    {
        var selectedFilesCount = Files.Count(f => f.IsSelected);
        SelectedFilesText = _language.GetString("Torrent.SelectedFilesFormat", selectedFilesCount, Files.Count);

        if (IsDownloading) return;

        var total = TorrentSelection.CountSelectedPieces(Torrent, Torrent.Files);
        ProgressMax = total > 0 ? total : 1;

        var dir = _getDownloadDirectory();
        ProgressValue = TorrentSelection.CountSavedSelectedPieces(Torrent, dir, Torrent.Files);
    }

    private void ToggleDownload()
    {
        if (IsDownloading) Stop();
        else Start();
    }

    public async void Start()
    {
        if (IsDownloading || !HasSelectedFiles) return;

        var downloadDir = _getDownloadDirectory();
        var savedPieces = TorrentSelection.CountSavedSelectedPieces(Torrent, downloadDir, Torrent.Files);

        IsDownloading = true;
        SetStatus(TorrentStatusKind.Connecting);
        ProgressValue = savedPieces;
        ResetMetrics(savedPieces);
        SpeedText = FormatSpeedText(_smoothedSpeed);
        EtaText = FormatEtaText(_publishedEta);

        _cts = new CancellationTokenSource();
        _download = new TorrentDownload(Torrent, downloadDir, _config, new UiTorrentLogger(LogMessage));

        StartTimer();

        bool completed = false;
        try
        {
            completed = await Task.Run(() => _download.RunAsync(_cts.Token), _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetErrorStatus(ex.Message);
        }
        finally
        {
            StopTimer();
            _cts?.Dispose();
            _cts = null;
            _download = null;
            IsDownloading = false;

            _speedSamples.Clear();
            _etaSamples.Clear();
            _smoothedSpeed = 0;
            SpeedText = FormatSpeedText(_smoothedSpeed);

            if (completed)
            {
                ProgressValue = ProgressMax;
                SetStatus(TorrentStatusKind.Completed);
                EtaText = FormatEtaText(TimeSpan.Zero);
            }
            else
            {
                EtaText = FormatIdleEtaText();
                if (_statusKind == TorrentStatusKind.Connecting || _statusKind == TorrentStatusKind.ActivePeers)
                    SetStatus(TorrentStatusKind.Stopped);
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _download?.Stop();
        _download?.FlushState();
    }

    private string FormatStatus() => _statusKind switch
    {
        TorrentStatusKind.Connecting => _language.GetString("Torrent.StatusConnecting"),
        TorrentStatusKind.ActivePeers => _language.GetString("Torrent.StatusActivePeersFormat", _statusActivePeers, _statusCompletedPieces, _statusTotalPieces),
        TorrentStatusKind.Completed => _language.GetString("Torrent.StatusCompleted"),
        TorrentStatusKind.Stopped => _language.GetString("Torrent.StatusStopped"),
        TorrentStatusKind.Error => _language.GetString("Torrent.StatusErrorFormat", _statusError),
        _ => string.Empty
    };

    private void SetStatus(TorrentStatusKind kind)
    {
        _statusKind = kind;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
    }

    private void SetActivePeerStatus(int activePeers, int completedPieces, int totalPieces)
    {
        _statusActivePeers = activePeers;
        _statusCompletedPieces = completedPieces;
        _statusTotalPieces = totalPieces;
        SetStatus(TorrentStatusKind.ActivePeers);
    }

    private void SetErrorStatus(string message)
    {
        _statusError = message;
        SetStatus(TorrentStatusKind.Error);
    }

    private string FormatSpeedText(double bytesPerSecond) =>
        $"{_language.GetString("Torrent.SpeedPrefix")} {SizeFormatter.FormatSpeed(bytesPerSecond, _language)}";

    private string FormatEtaText(TimeSpan? eta) =>
        $"{_language.GetString("Torrent.EtaPrefix")} {SizeFormatter.FormatEta(eta, _language)}";

    private string FormatIdleEtaText() => $"{_language.GetString("Torrent.EtaPrefix")} —";

    private void RefreshEtaText()
    {
        if (_statusKind == TorrentStatusKind.Completed)
            EtaText = FormatEtaText(TimeSpan.Zero);
        else if (IsDownloading)
            EtaText = FormatEtaText(_publishedEta);
        else
            EtaText = FormatIdleEtaText();
    }

    private void ResetMetrics(int initialCompletedPieces)
    {
        _speedSamples.Clear();
        _etaSamples.Clear();
        _baselineBytes = Math.Max(0, (long)initialCompletedPieces * Torrent.PieceLength);
        _metricsInitialized = false;
        _smoothedSpeed = 0;
        _publishedEta = null;
        _lastEtaPublishTime = DateTime.UtcNow;
    }

    private void UpdateMetrics(int completedPieces, int totalPieces)
    {
        var now = DateTime.UtcNow;
        var pieceLength = Torrent.PieceLength;
        var currentBytes = Math.Max(0, (long)completedPieces * pieceLength);

        if (!_metricsInitialized || currentBytes < _baselineBytes)
        {
            _baselineBytes = currentBytes;
            _metricsInitialized = true;
            _speedSamples.Clear();
            _etaSamples.Clear();
            _speedSamples.Enqueue((now, currentBytes));
            _lastEtaPublishTime = now;
            return;
        }

        _speedSamples.Enqueue((now, currentBytes));

        while (_speedSamples.Count > 2 && now - _speedSamples.Peek().Timestamp > SpeedWindowDuration)
            _speedSamples.Dequeue();

        var oldest = _speedSamples.Peek();
        var elapsedSeconds = (now - oldest.Timestamp).TotalSeconds;
        var deltaBytes = Math.Max(0, currentBytes - oldest.Bytes);

        var instantSpeed = elapsedSeconds >= 0.4 ? deltaBytes / elapsedSeconds : 0;
        _smoothedSpeed = _smoothedSpeed <= 0
            ? instantSpeed
            : (_smoothedSpeed * 0.7) + (instantSpeed * 0.3);

        var remainingPieces = Math.Max(0, totalPieces - completedPieces);
        if (remainingPieces == 0)
        {
            _etaSamples.Clear();
            _publishedEta = TimeSpan.Zero;
        }
        else
        {
            if (_smoothedSpeed >= 1)
            {
                var remainingBytes = (long)remainingPieces * pieceLength;
                var rawEtaSeconds = remainingBytes / _smoothedSpeed;

                if (rawEtaSeconds <= TimeSpan.FromDays(30).TotalSeconds)
                    _etaSamples.Add(rawEtaSeconds);
            }

            if (now - _lastEtaPublishTime >= EtaPublishInterval)
            {
                if (_etaSamples.Count > 0)
                {
                    _etaSamples.Sort();
                    var mid = _etaSamples.Count / 2;
                    var medianSeconds = _etaSamples.Count % 2 != 0
                        ? _etaSamples[mid]
                        : (_etaSamples[mid - 1] + _etaSamples[mid]) / 2.0;

                    _publishedEta = TimeSpan.FromSeconds(medianSeconds);
                    _etaSamples.Clear();
                }
                else
                {
                    _publishedEta = null;
                }

                _lastEtaPublishTime = now;
            }
        }

        SpeedText = FormatSpeedText(_smoothedSpeed);
        EtaText = FormatEtaText(_publishedEta);
    }

    private void StartTimer()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (s, e) =>
        {
            if (_download == null) return;
            ProgressMax = _download.TotalPieces > 0 ? _download.TotalPieces : ProgressMax;
            ProgressValue = _download.CompletedPieces;

            UpdateMetrics(ProgressValue, ProgressMax);

            if (IsDownloading && _cts != null && !_cts.IsCancellationRequested)
                SetActivePeerStatus(_download.ActivePeerCount, ProgressValue, ProgressMax);
        };
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void LogMessage(string msg) { }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
