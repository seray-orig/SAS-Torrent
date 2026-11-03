using System.Collections;
using System.Security.Cryptography;
using SAS_Torrent.Console.IO;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Peers;

public sealed class DownloadManager
{
    private readonly TorrentInfo _torrent;
    private readonly PeerManager _peerManager;
    private readonly FileWriter _fileWriter;
    private readonly BitArray _havePieces;
    private readonly int _pieceCount;
    private readonly long _pieceLength;
    private readonly object _lock = new();
    private int _completedPieces;
    private readonly PieceAssembler _assembler;

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

    private void OnPieceCompleted(int pieceIndex)
    {
        var assembled = _assembler.GetAssembled(pieceIndex);
        if (assembled == null) return;

        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(assembled);
        var expected = _torrent.Pieces.AsSpan(pieceIndex * 20, 20);

        bool ok = true;
        for (int i = 0; i < 20; i++)
        {
            if (hash[i] != expected[i])
            {
                ok = false;
                break;
            }
        }

        if (ok)
        {
            _fileWriter.WritePiece(pieceIndex, 0, assembled);
            lock (_lock)
            {
                if (!_havePieces[pieceIndex])
                {
                    _havePieces[pieceIndex] = true;
                    _completedPieces++;
                    System.Console.WriteLine($"[DL] Кусок {pieceIndex} проверен и записан. Прогресс: {_completedPieces}/{_pieceCount}");
                }
            }
        }
        else
        {
            System.Console.WriteLine($"[DL] Кусок {pieceIndex} не прошёл проверку SHA1, перезапрашиваем");
            _assembler.ResetPiece(pieceIndex);
            lock (_lock)
            {
                _havePieces[pieceIndex] = false;
            }
        }
    }

    public async Task DownloadAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var seeders = _peerManager.GetSeeders().Where(s => s.PeerChoking == false).ToList();
            if (seeders.Count == 0)
            {
                await Task.Delay(500, ct);
                continue;
            }

            int nextPiece = -1;
            lock (_lock)
            {
                for (int piece = 0; piece < _pieceCount; piece++)
                {
                    if (!_havePieces[piece])
                    {
                        nextPiece = piece;
                        break;
                    }
                }
            }

            if (nextPiece == -1)
                break;

            var pieceSize = GetPieceSize(nextPiece);
            var blockSize = 16384;
            var numBlocks = (int)Math.Ceiling((double)pieceSize / blockSize);
            var seeder = seeders[nextPiece % seeders.Count];
            seeder.OnPiece -= OnPieceReceived;
            seeder.OnPiece += OnPieceReceived;

            for (int b = 0; b < numBlocks; b++)
            {
                var begin = b * blockSize;
                var reqSize = Math.Min(blockSize, pieceSize - begin);
                _ = seeder.SendAsync(PeerWireMessages.BuildRequest(nextPiece, begin, reqSize), ct);
            }

            await Task.Delay(200, ct);
        }

        System.Console.WriteLine($"Загрузка завершена. Кусков: {_completedPieces}/{_pieceCount}");
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
}