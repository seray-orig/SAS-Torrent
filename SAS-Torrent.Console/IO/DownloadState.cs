using System.Globalization;
using System.Text;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.IO;

public sealed class DownloadState
{
    private const string FormatVersion = "v1";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(1);

    private readonly string _path;
    private readonly string _infoHash;
    private readonly long _pieceLength;
    private readonly long _totalLength;
    private readonly HashSet<int> _pieces;
    private readonly object _lock = new();
    private DateTime _lastSave = DateTime.MinValue;
    private bool _deleted;

    private DownloadState(string path, TorrentInfo torrent, IEnumerable<int> pieces)
    {
        _path = path;
        _infoHash = Convert.ToHexString(torrent.InfoHash);
        _pieceLength = torrent.PieceLength;
        _totalLength = torrent.TotalLength;
        _pieces = new HashSet<int>(pieces);
    }

    public static DownloadState Load(TorrentInfo torrent)
    {
        var path = GetPath(torrent);
        var pieces = Array.Empty<int>();

        try
        {
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length >= 5 &&
                    lines[0] == FormatVersion &&
                    lines[1] == Convert.ToHexString(torrent.InfoHash) &&
                    long.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pieceLength) &&
                    pieceLength == torrent.PieceLength &&
                    long.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalLength) &&
                    totalLength == torrent.TotalLength)
                {
                    var pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);
                    pieces = lines[4]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var piece) ? piece : -1)
                        .Where(piece => piece >= 0 && piece < pieceCount)
                        .Distinct()
                        .ToArray();
                }
            }
        }
        catch { }

        return new DownloadState(path, torrent, pieces);
    }

    public static string GetPath(TorrentInfo torrent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SAS-Torrent");
        var fileName = Convert.ToHexString(torrent.InfoHash).ToLowerInvariant() + ".progress";
        return Path.Combine(directory, fileName);
    }

    public string FilePath => _path;

    public IReadOnlyList<int> Pieces
    {
        get { lock (_lock) return _pieces.OrderBy(piece => piece).ToArray(); }
    }

    public void MarkCompleted(int pieceIndex)
    {
        bool save;
        lock (_lock)
        {
            if (_deleted || !_pieces.Add(pieceIndex)) return;
            save = DateTime.UtcNow - _lastSave >= SaveInterval;
        }

        if (save)
            Save();
    }

    public void Flush() => Save();

    public void Delete()
    {
        lock (_lock)
        {
            if (_deleted) return;
            _deleted = true;
            _pieces.Clear();
            try { File.Delete(_path); } catch { }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            if (_deleted) return;

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                builder.AppendLine(FormatVersion);
                builder.AppendLine(_infoHash);
                builder.AppendLine(_pieceLength.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine(_totalLength.ToString(CultureInfo.InvariantCulture));
                builder.Append(string.Join(',', _pieces.OrderBy(piece => piece)));

                var temp = _path + ".tmp";
                File.WriteAllText(temp, builder.ToString());
                File.Move(temp, _path, overwrite: true);
                _lastSave = DateTime.UtcNow;
            }
            catch { }
        }
    }
}
