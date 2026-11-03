using System.Collections;
using System.Security.Cryptography;
using SAS_Torrent.Console.IO;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Peers;

public sealed class DownloadManager
{
    private const int BlockSize = 16384;
    private const int BlocksPerPeerBudget = 200;
    private const int MinPiecesPerPeer = 1;
    private const int MaxPiecesPerPeer = 16;
    private const int MinInFlightPieces = 4;
    private const int MaxInFlightPieces = 128;
    private static readonly TimeSpan PieceTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PieceStallTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeerBanTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);

    private readonly TorrentInfo _torrent;
    private readonly PeerManager _peerManager;
    private readonly FileWriter _fileWriter;
    private readonly BitArray _havePieces;
    private readonly int _pieceCount;
    private readonly long _pieceLength;
    private readonly int _blocksPerPiece;
    private readonly int _maxInFlightPieces;
    private readonly object _lock = new();
    private readonly Dictionary<int, InFlightPiece> _inFlight = new();
    private readonly Dictionary<int, Dictionary<PeerConnection, DateTime>> _pieceBans = new();
    private readonly Dictionary<PeerConnection, PeerStats> _peerStats = new();
    private readonly HashSet<PeerConnection> _subscribed = new();
    private readonly SemaphoreSlim _workSignal = new(0);
    private readonly PieceAssembler _assembler;
    private int _completedPieces;
    private int _nextPiece;
    private bool _loggedWaiting;

    public DownloadManager(TorrentInfo torrent, PeerManager peerManager, FileWriter fileWriter)
    {
        _torrent = torrent;
        _peerManager = peerManager;
        _fileWriter = fileWriter;
        _pieceLength = torrent.PieceLength;
        _pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / _pieceLength);
        _blocksPerPiece = (int)Math.Ceiling((double)_pieceLength / BlockSize);
        _maxInFlightPieces = ComputeMaxInFlightPieces(_pieceLength);
        _havePieces = new BitArray(_pieceCount);
        _assembler = new PieceAssembler(_pieceCount, _pieceLength, _torrent.TotalLength);
        _assembler.OnPieceCompleted += OnPieceCompleted;
    }

    public int CompletedPieces
    {
        get { lock (_lock) return _completedPieces; }
    }

    public async Task<bool> DownloadAsync(CancellationToken ct)
    {
        var lastCompleted = 0;
        var idleSince = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var seeders = _peerManager.GetSeeders().Where(s => !s.PeerChoking).ToList();
            EnsureSubscribed(seeders);

            lock (_lock)
            {
                ReleaseTimedOutPiecesLocked();
                if (_completedPieces != lastCompleted)
                {
                    lastCompleted = _completedPieces;
                    idleSince = DateTime.UtcNow;
                }

                if (_completedPieces >= _pieceCount)
                    return true;
            }

            if (seeders.Count > 0)
            {
                _loggedWaiting = false;
                SchedulePieces(seeders, ct);
            }
            else if (!_loggedWaiting)
            {
                _loggedWaiting = true;
                System.Console.WriteLine("Ожидание сидов: подключение и поиск продолжаются параллельно...");
            }

            if (DateTime.UtcNow - idleSince > IdleTimeout)
            {
                bool nothingInFlight;
                lock (_lock)
                    nothingInFlight = _inFlight.Count == 0;

                if (nothingInFlight)
                {
                    System.Console.WriteLine($"Нет прогресса загрузки более {IdleTimeout.TotalSeconds:0} секунд.");
                    return false;
                }
            }

            try
            {
                await _workSignal.WaitAsync(TimeSpan.FromMilliseconds(250), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    private void SchedulePieces(List<PeerConnection> seeders, CancellationToken ct)
    {
        lock (_lock)
        {
            var perPeer = new Dictionary<PeerConnection, int>();
            foreach (var seeder in seeders)
                perPeer[seeder] = 0;
            foreach (var inFlight in _inFlight.Values)
                if (perPeer.ContainsKey(inFlight.Peer))
                    perPeer[inFlight.Peer]++;

            var maxDepth = MaxDepthForPeer();
            var startIndex = 0;
            while (_inFlight.Count < _maxInFlightPieces)
            {
                PeerConnection? target = null;
                var piece = -1;
                for (var i = 0; i < seeders.Count; i++)
                {
                    var candidate = seeders[(startIndex + i) % seeders.Count];
                    var candidateLimit = Math.Min(maxDepth, GetStats(candidate).Depth);
                    if (perPeer[candidate] >= candidateLimit) continue;

                    var candidatePiece = FindNextPieceLocked(candidate);
                    if (candidatePiece < 0) continue;

                    target = candidate;
                    piece = candidatePiece;
                    startIndex = (startIndex + i + 1) % seeders.Count;
                    break;
                }

                if (target == null) break;

                _inFlight[piece] = new InFlightPiece(target);
                perPeer[target]++;
                RequestMissingBlocks(target, piece, ct);
            }
        }
    }

    private PeerStats GetStats(PeerConnection conn)
    {
        if (!_peerStats.TryGetValue(conn, out var stats))
        {
            stats = new PeerStats(Math.Min(2, MaxDepthForPeer()));
            _peerStats[conn] = stats;
        }
        return stats;
    }

    private int MaxDepthForPeer()
    {
        return Math.Clamp(BlocksPerPeerBudget / Math.Max(_blocksPerPiece, 1), MinPiecesPerPeer, MaxPiecesPerPeer);
    }

    private static int ComputeMaxInFlightPieces(long pieceLength)
    {
        var availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var budget = Math.Clamp(availableMemory / 16, 16L * 1024 * 1024, 128L * 1024 * 1024);
        var byMemory = (int)(budget / Math.Max(pieceLength, BlockSize));
        return Math.Clamp(byMemory, MinInFlightPieces, MaxInFlightPieces);
    }

    private void EnsureSubscribed(List<PeerConnection> seeders)
    {
        lock (_lock)
        {
            _subscribed.RemoveWhere(conn =>
            {
                if (conn.IsConnected) return false;
                conn.OnPiece -= OnPieceReceived;
                _peerStats.Remove(conn);
                RemoveBansLocked(conn);
                return true;
            });

            var added = false;
            foreach (var seeder in seeders)
            {
                if (!_subscribed.Add(seeder)) continue;
                seeder.OnPiece += OnPieceReceived;
                added = true;
            }

            if (added)
                _workSignal.Release();
        }
    }

    private int FindNextPieceLocked(PeerConnection peer)
    {
        for (var step = 0; step < _pieceCount; step++)
        {
            var piece = (_nextPiece + step) % _pieceCount;
            if (_havePieces[piece] || _inFlight.ContainsKey(piece)) continue;
            if (IsBannedLocked(piece, peer)) continue;
            _nextPiece = (piece + 1) % _pieceCount;
            return piece;
        }
        return -1;
    }

    private bool IsBannedLocked(int piece, PeerConnection peer)
    {
        if (!_pieceBans.TryGetValue(piece, out var bans)) return false;
        if (!bans.TryGetValue(peer, out var until)) return false;
        if (until > DateTime.UtcNow) return true;

        bans.Remove(peer);
        if (bans.Count == 0)
            _pieceBans.Remove(piece);
        return false;
    }

    private void BanPeerLocked(int piece, PeerConnection peer)
    {
        if (!_pieceBans.TryGetValue(piece, out var bans))
        {
            bans = new Dictionary<PeerConnection, DateTime>();
            _pieceBans[piece] = bans;
        }

        bans[peer] = DateTime.UtcNow + PeerBanTimeout;
    }

    private void RemoveBansLocked(PeerConnection peer)
    {
        foreach (var (piece, bans) in _pieceBans.ToList())
        {
            bans.Remove(peer);
            if (bans.Count == 0)
                _pieceBans.Remove(piece);
        }
    }

    private void RequestMissingBlocks(PeerConnection peer, int pieceIndex, CancellationToken ct)
    {
        var pieceSize = GetPieceSize(pieceIndex);
        for (var begin = 0; begin < pieceSize; begin += BlockSize)
        {
            if (_assembler.HasBlock(pieceIndex, begin)) continue;
            var requestSize = Math.Min(BlockSize, pieceSize - begin);
            _ = peer.SendAsync(PeerWireMessages.BuildRequest(pieceIndex, begin, requestSize), ct);
        }
    }

    private void ReleaseTimedOutPiecesLocked()
    {
        if (_inFlight.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var (piece, inFlight) in _inFlight.ToList())
        {
            var reason = !inFlight.Peer.IsConnected
                ? "disconnected"
                : inFlight.Peer.PeerChoking
                    ? "choked"
                    : now - inFlight.LastProgressAt >= PieceStallTimeout
                        ? "stall"
                        : now - inFlight.StartedAt >= PieceTimeout
                            ? "timeout"
                            : null;
            if (reason == null) continue;

            _inFlight.Remove(piece);
            BanPeerLocked(piece, inFlight.Peer);
            if (_peerStats.TryGetValue(inFlight.Peer, out var stats))
                stats.Depth = Math.Max(MinPiecesPerPeer, stats.Depth / 2);

            System.Console.WriteLine(
                $"[DL] Piece {piece} released from {inFlight.Peer.Ip}:{inFlight.Peer.Port} " +
                $"({inFlight.Peer.TransportName}), reason={reason}");
        }
    }

    private void OnPieceCompleted(int pieceIndex)
    {
        var assembled = _assembler.GetAssembled(pieceIndex);
        if (assembled == null) return;

        if (VerifyPiece(pieceIndex, assembled))
        {
            _fileWriter.WritePiece(pieceIndex, 0, assembled);

            lock (_lock)
            {
                if (_inFlight.TryGetValue(pieceIndex, out var inFlight))
                {
                    if (DateTime.UtcNow - inFlight.StartedAt < TimeSpan.FromSeconds(5) &&
                        _peerStats.TryGetValue(inFlight.Peer, out var stats))
                        stats.Depth = Math.Min(MaxDepthForPeer(), stats.Depth + 1);
                }

                _inFlight.Remove(pieceIndex);
                _pieceBans.Remove(pieceIndex);
                if (!_havePieces[pieceIndex])
                {
                    _havePieces[pieceIndex] = true;
                    _completedPieces++;
                }
            }

            _assembler.ReleasePiece(pieceIndex);
            System.Console.WriteLine($"[DL] Кусок {pieceIndex} проверен и записан. Прогресс: {CompletedPieces}/{_pieceCount}");
        }
        else
        {
            System.Console.WriteLine($"[DL] Кусок {pieceIndex} не прошёл проверку SHA1, перезапрашиваем");
            _assembler.ResetPiece(pieceIndex);
            lock (_lock)
            {
                if (_inFlight.TryGetValue(pieceIndex, out var inFlight) &&
                    _peerStats.TryGetValue(inFlight.Peer, out var stats))
                    stats.Depth = Math.Max(MinPiecesPerPeer, stats.Depth / 2);
                _inFlight.Remove(pieceIndex);
            }
        }

        _workSignal.Release();
    }

    private bool VerifyPiece(int pieceIndex, byte[] assembled)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(assembled);
        var expected = _torrent.Pieces.AsSpan(pieceIndex * 20, 20);
        return hash.AsSpan().SequenceEqual(expected);
    }

    private void OnPieceReceived(PeerConnection conn, byte[] msg)
    {
        try
        {
            if (msg.Length < 9) return;
            int index = (msg[1] << 24) | (msg[2] << 16) | (msg[3] << 8) | msg[4];
            int begin = (msg[5] << 24) | (msg[6] << 16) | (msg[7] << 8) | msg[8];
            var data = new byte[msg.Length - 9];
            Array.Copy(msg, 9, data, 0, data.Length);

            lock (_lock)
            {
                if (_inFlight.TryGetValue(index, out var inFlight))
                    inFlight.LastProgressAt = DateTime.UtcNow;
            }

            _assembler.AddBlock(new PieceBlock { PieceIndex = index, Begin = begin, Data = data });
        }
        catch { }
    }

    private int GetPieceSize(int pieceIndex)
    {
        if (pieceIndex == _pieceCount - 1)
        {
            var last = _torrent.TotalLength % _pieceLength;
            return last == 0 ? (int)_pieceLength : (int)last;
        }
        return (int)_pieceLength;
    }

    private sealed class InFlightPiece
    {
        public InFlightPiece(PeerConnection peer)
        {
            Peer = peer;
            StartedAt = DateTime.UtcNow;
            LastProgressAt = StartedAt;
        }

        public PeerConnection Peer { get; }
        public DateTime StartedAt { get; }
        public DateTime LastProgressAt { get; set; }
    }

    private sealed class PeerStats
    {
        public int Depth { get; set; }

        public PeerStats(int depth)
        {
            Depth = depth;
        }
    }
}
