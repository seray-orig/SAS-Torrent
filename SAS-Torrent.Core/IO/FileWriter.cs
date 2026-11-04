using System.Security.Cryptography;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Core.IO;

public sealed class FileWriter
{
    private readonly TorrentInfo _torrent;
    private readonly string _downloadDir;
    private readonly long _pieceLength;
    private readonly int _pieceCount;
    private readonly FileStream?[] _files;
    private readonly long[] _fileOffsets;
    private readonly object _writeLock = new();
    public int SelectedPieceCount { get; }

    public FileWriter(TorrentInfo torrent, string downloadDir)
    {
        _torrent = torrent;
        _downloadDir = Path.Combine(downloadDir, SanitizeName(torrent.Name));
        _pieceLength = torrent.PieceLength;
        _pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / _pieceLength);

        _files = new FileStream?[torrent.Files.Count];
        _fileOffsets = BuildOffsets();

        SelectedPieceCount = ComputeSelectedPieceCount();

        Directory.CreateDirectory(_downloadDir);
        for (int i = 0; i < torrent.Files.Count; i++)
        {
            if (!torrent.Files[i].IsSelected) continue;

            var path = Path.Combine(_downloadDir, SanitizeName(torrent.Files[i].RelativePath));
            _files[i] = OpenTarget(path, torrent.Files[i].Length);
        }
    }

    private int ComputeSelectedPieceCount()
    {
        var count = 0;
        for (int p = 0; p < _pieceCount; p++)
            if (IsPieceRelevant(p)) count++;
        return count;
    }

    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        sanitized = sanitized.TrimEnd('.');
        return string.IsNullOrEmpty(sanitized) ? "torrent" : sanitized;
    }

    private static FileStream OpenTarget(string path, long length)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        if (stream.Length != length)
            stream.SetLength(length);
        return stream;
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

    public bool IsPieceRelevant(int pieceIndex)
    {
        var start = (long)pieceIndex * _pieceLength;
        var pieceSize = GetPieceSize(pieceIndex);
        var end = start + pieceSize;

        for (int i = 0; i < _torrent.Files.Count; i++)
        {
            if (!_torrent.Files[i].IsSelected) continue;
            var fileStart = _fileOffsets[i];
            var fileEnd = fileStart + _torrent.Files[i].Length;
            if (start < fileEnd && end > fileStart)
                return true;
        }

        return false;
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
            if (_files[i] == null) continue;

            var fileStart = _fileOffsets[i];
            var fileEnd = fileStart + _torrent.Files[i].Length;
            if (pos >= fileEnd) continue;
            if (pos + remaining <= fileStart) continue;
            var writeStart = Math.Max(pos, fileStart);
            var writeEnd = Math.Min(pos + remaining, fileEnd);
            var writeLen = (int)(writeEnd - writeStart);
            if (writeLen <= 0) continue;
            _files[i]!.Seek(writeStart - fileStart, SeekOrigin.Begin);
            _files[i]!.Write(data, dataOffset + (int)(writeStart - pos), writeLen);
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
            if (_files[i] == null) continue;

            var fileStart = _fileOffsets[i];
            var fileEnd = fileStart + _torrent.Files[i].Length;
            if (pos >= fileEnd) continue;
            if (pos + remaining <= fileStart) continue;
            var readStart = Math.Max(pos, fileStart);
            var readEnd = Math.Min(pos + remaining, fileEnd);
            var readLen = (int)(readEnd - readStart);
            if (readLen <= 0) continue;
            _files[i]!.Seek(readStart - fileStart, SeekOrigin.Begin);
            _files[i]!.ReadExactly(buffer, bufOffset + (int)(readStart - pos), readLen);
            remaining -= readLen;
            pos = readEnd;
        }
    }

    public void Close()
    {
        foreach (var f in _files)
            if (f != null) try { f.Dispose(); } catch { }
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
