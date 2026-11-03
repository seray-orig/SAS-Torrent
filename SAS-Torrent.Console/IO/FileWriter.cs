using System.Security.Cryptography;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.IO;

public sealed class FileWriter
{
    private readonly TorrentInfo _torrent;
    private readonly string _downloadDir;
    private readonly long _pieceLength;
    private readonly int _pieceCount;
    private readonly FileStream[] _files;
    private readonly long[] _fileOffsets;
    private readonly object _writeLock = new();

    public FileWriter(TorrentInfo torrent, string downloadDir)
    {
        _torrent = torrent;
        _downloadDir = downloadDir;
        _pieceLength = torrent.PieceLength;
        _pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / _pieceLength);
        Directory.CreateDirectory(downloadDir);
        if (torrent.IsMultiFile)
        {
            foreach (var f in torrent.Files)
            {
                var fullPath = Path.Combine(downloadDir, f.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var fs = File.Create(fullPath);
                fs.SetLength(f.Length);
            }
            _files = torrent.Files.Select(f => new FileStream(Path.Combine(downloadDir, f.RelativePath), FileMode.Open, FileAccess.ReadWrite)).ToArray();
            _fileOffsets = BuildOffsets();
        }
        else
        {
            var fullPath = Path.Combine(downloadDir, torrent.Name);
            using var fs = File.Create(fullPath);
            fs.SetLength(torrent.TotalLength);
            _files = new[] { new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite) };
            _fileOffsets = new long[] { 0 };
        }
    }

    private long[] BuildOffsets()
    {
        var offsets = new long[_torrent.Files.Count];
        long cur = 0;
        for (int i = 0; i < _torrent.Files.Count; i++)
        {
            offsets[i] = cur;
            cur += _torrent.Files[i].Length;
        }
        return offsets;
    }

    public void WritePiece(int pieceIndex, int begin, byte[] data)
    {
        var pieceOffset = (long)pieceIndex * _pieceLength + begin;
        lock (_writeLock)
            WriteAt(pieceOffset, data);
    }

    private void WriteAt(long offset, byte[] data)
    {
        var remaining = data.Length;
        var dataOffset = 0;
        long pos = offset;
        for (int i = 0; i < _files.Length && remaining > 0; i++)
        {
            var fileStart = _fileOffsets[i];
            var fileEnd = fileStart + _torrent.Files[i].Length;
            if (pos >= fileEnd) continue;
            if (pos + remaining <= fileStart) continue;
            var writeStart = Math.Max(pos, fileStart);
            var writeEnd = Math.Min(pos + remaining, fileEnd);
            var writeLen = (int)(writeEnd - writeStart);
            if (writeLen <= 0) continue;
            _files[i].Seek(writeStart - fileStart, SeekOrigin.Begin);
            _files[i].Write(data, dataOffset + (int)(writeStart - pos), writeLen);
            remaining -= writeLen;
            pos = writeEnd;
        }
    }

    public bool VerifyPiece(int pieceIndex)
    {
        var start = (long)pieceIndex * _pieceLength;
        var length = (int)Math.Min(_pieceLength, _torrent.TotalLength - start);
        var buf = new byte[length];
        ReadAt(start, buf);
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(buf);
        var expected = _torrent.Pieces.AsSpan(pieceIndex * 20, 20);
        for (int i = 0; i < 20; i++)
            if (hash[i] != expected[i]) return false;
        return true;
    }

    private void ReadAt(long offset, byte[] buffer)
    {
        var remaining = buffer.Length;
        var bufOffset = 0;
        long pos = offset;
        for (int i = 0; i < _files.Length && remaining > 0; i++)
        {
            var fileStart = _fileOffsets[i];
            var fileEnd = fileStart + _torrent.Files[i].Length;
            if (pos >= fileEnd) continue;
            if (pos + remaining <= fileStart) continue;
            var readStart = Math.Max(pos, fileStart);
            var readEnd = Math.Min(pos + remaining, fileEnd);
            var readLen = (int)(readEnd - readStart);
            if (readLen <= 0) continue;
            _files[i].Seek(readStart - fileStart, SeekOrigin.Begin);
            _files[i].ReadExactly(buffer, bufOffset + (int)(readStart - pos), readLen);
            remaining -= readLen;
            pos = readEnd;
        }
    }

    public void Close()
    {
        foreach (var f in _files) try { f.Dispose(); } catch { }
    }
}
