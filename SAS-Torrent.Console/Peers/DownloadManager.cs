using System.Collections;
using System.Security.Cryptography;
using SAS_Torrent.Console.IO;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Peers;

public sealed class DownloadManager
{
    private const int BlockSize = 16384;
    private const int MaxInFlightPieces = 64;
    private const int MaxPiecesPerPeer = 4;
    private static readonly TimeSpan PieceTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);

    private readonly TorrentInfo _torrent;
    private readonly PeerManager _peerManager;
    private readonly FileWriter _fileWriter;
    private readonly BitArray _havePieces;
    private readonly int _pieceCount;
    private readonly long _pieceLength;
    private readonly object _lock = new();
    private readonly Dictionary<int, InFlightPiece> _inFlight = new();
    private readonly HashSet<PeerConnection> _subscribed = new();
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

            await Task.Delay(200, ct);
        }

        return false;
    }

    private void SchedulePieces(List<PeerConnection> seeders, CancellationToken ct)
    {
        lock (_lock)
        {
            var perPeer = seeders.ToDictionary(s => s, _ => 0);
            foreach (var inFlight in _inFlight.Values)
                if (perPeer.ContainsKey(inFlight.Peer))
                    perPeer[inFlight.Peer]++;

            var startIndex = 0;
            while (_inFlight.Count < MaxInFlightPieces)
            {
                PeerConnection? target = null;
                for (var i = 0; i < seeders.Count; i++)
                {
                    var candidate = seeders[(startIndex + i) % seeders.Count];
                    if (perPeer[candidate] < MaxPiecesPerPeer)
                    {
                        target = candidate;
                        startIndex = (startIndex + i + 1) % seeders.Count;
                        break;
                    }
                }

                if (target == null) break;

                var piece = FindNextPieceLocked();
                if (piece < 0) break;

                _inFlight[piece] = new InFlightPiece(target, DateTime.UtcNow);
                perPeer[target]++;
                RequestMissingBlocks(target, piece, ct);
            }
        }
    }

    private void EnsureSubscribed(List<PeerConnection> seeders)
    {
        lock (_lock)
        {
            _subscribed.RemoveWhere(conn =>
            {
                if (conn.IsConnected) return false;
                conn.OnPiece -= OnPieceReceived;
                return true;
            });

            foreach (var seeder in seeders)
            {
                if (_subscribed.Add(seeder))
                    seeder.OnPiece += OnPieceReceived;
            }
        }
    }

    private int FindNextPieceLocked()
    {
        for (var step = 0; step < _pieceCount; step++)
        {
            var piece = (_nextPiece + step) % _pieceCount;
            if (_havePieces[piece] || _inFlight.ContainsKey(piece)) continue;
            _nextPiece = (piece + 1) % _pieceCount;
            return piece;
        }
        return -1;
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
            if (inFlight.Peer.IsConnected && now - inFlight.StartedAt < PieceTimeout) continue;
            _inFlight.Remove(piece);
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
                _inFlight.Remove(pieceIndex);
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
                _inFlight.Remove(pieceIndex);
        }
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

    private readonly struct InFlightPiece
    {
        public PeerConnection Peer { get; }
        public DateTime StartedAt { get; }

        public InFlightPiece(PeerConnection peer, DateTime startedAt)
        {
            Peer = peer;
            StartedAt = startedAt;
        }
    }
}
