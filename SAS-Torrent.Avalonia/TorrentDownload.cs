using SAS_Torrent.Core.Config;
using SAS_Torrent.Core.IO;
using SAS_Torrent.Core.Logging;
using SAS_Torrent.Core.Peers;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public sealed class TorrentDownload
{
    private readonly TorrentInfo _torrent;
    private readonly string _downloadDir;
    private readonly AppConfig _config;
    private readonly ITorrentLogger? _logger;

    private FileWriter? _fileWriter;
    private PeerManager? _peerManager;
    private PeerDiscovery? _discovery;
    private PeerInboundListener? _inboundListener;
    private DownloadState? _state;
    private DownloadManager? _manager;
    private Task? _connectTask;
    private CancellationTokenSource? _cts;

    public TorrentDownload(TorrentInfo torrent, string downloadDir, AppConfig config, ITorrentLogger? logger = null)
    {
        _torrent = torrent;
        _downloadDir = downloadDir;
        _config = config;
        _logger = logger;
    }

    public int TotalPieces { get; private set; }

    public int CompletedPieces => _manager?.CompletedPieces ?? 0;

    public int ActivePeerCount => _manager?.ActivePeerCount ?? 0;

    public void Stop() => _cts?.Cancel();

    public void FlushState() => _state?.Flush();

    public async Task<bool> RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = linked;
        var token = linked.Token;

        try
        {
            token.ThrowIfCancellationRequested();

            Directory.CreateDirectory(_downloadDir);

            var pieceCount = TorrentSelection.GetPieceCount(_torrent);
            _fileWriter = new FileWriter(_torrent, _downloadDir);
            TotalPieces = _fileWriter.SelectedPieceCount;

            var journal = new TorrentJournal(_torrent.Name, _torrent.TotalLength, TotalPieces);
            var peerId = _config.CreatePeerId();
            _inboundListener = PeerInboundListener.Start(6881, _torrent.InfoHash, peerId, _logger);
            var port = _inboundListener.Port;

            var peerManager = new PeerManager(_torrent.InfoHash, peerId, pieceCount, _logger, journal);
            _peerManager = peerManager;

            var discovery = new PeerDiscovery(
                _torrent,
                peerId,
                port,
                _config.ResolvedUserAgent,
                _config.ResolvedDhtBootstrapHosts,
                _logger);
            _discovery = discovery;
            _state = DownloadState.Load(_torrent, _downloadDir);
            _manager = new DownloadManager(_torrent, peerManager, _fileWriter, _state, _logger, journal);

            _connectTask = Task.Run(
                () => peerManager.ConnectToPeersAsync(discovery.DiscoverAsync(token), token),
                token);

            return await _manager.DownloadAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _cts = null;
            linked.Cancel();

            _inboundListener?.Dispose();
            _inboundListener = null;

            _state?.Flush();
            _fileWriter?.Close();

            if (_peerManager != null)
            {
                _peerManager.DisconnectAll();

                if (_connectTask != null)
                {
                    _ = _connectTask.ContinueWith(
                        task => _ = task.Exception,
                        TaskContinuationOptions.OnlyOnFaulted);

                    try
                    {
                        await _connectTask.WaitAsync(TimeSpan.FromSeconds(1));
                    }
                    catch
                    {
                    }
                }

                _peerManager.DisconnectAll();
            }

            if (_discovery != null)
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await _discovery.AnnounceStoppedAsync(stopCts.Token); } catch (OperationCanceledException) { }
                _discovery = null;
            }
        }
    }
}
