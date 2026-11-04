using SAS_Torrent.Core.IO;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Avalonia;

public static class TorrentSelection
{
    public static int GetPieceCount(TorrentInfo torrent) =>
        (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);

    public static int CountSelectedPieces(TorrentInfo torrent, IReadOnlyList<TorrentFile> files)
    {
        var ranges = BuildSelectedPieceRanges(torrent, files);
        return CountPiecesInRanges(ranges);
    }

    public static int CountSavedSelectedPieces(TorrentInfo torrent, string downloadDir, IReadOnlyList<TorrentFile> files)
    {
        if (string.IsNullOrWhiteSpace(downloadDir) || !Directory.Exists(downloadDir))
            return 0;

        var ranges = BuildSelectedPieceRanges(torrent, files);
        if (ranges.Count == 0)
            return 0;

        var state = DownloadState.Load(torrent, downloadDir);
        var count = 0;
        foreach (var piece in state.Pieces)
            if (IsPieceInRanges(piece, ranges))
                count++;

        return count;
    }

    private static List<PieceRange> BuildSelectedPieceRanges(TorrentInfo torrent, IReadOnlyList<TorrentFile> files)
    {
        var byteRanges = new List<(long Start, long End)>();
        long offset = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var start = offset;
            var end = offset + file.Length;
            offset = end;

            if (!file.IsSelected || file.Length <= 0)
                continue;

            if (byteRanges.Count > 0 && byteRanges[^1].End == start)
                byteRanges[^1] = (byteRanges[^1].Start, end);
            else
                byteRanges.Add((start, end));
        }

        var pieceLength = torrent.PieceLength;
        var result = new List<PieceRange>(byteRanges.Count);
        foreach (var (start, end) in byteRanges)
        {
            var firstPiece = (int)(start / pieceLength);
            var lastPiece = (int)((end - 1) / pieceLength);

            if (result.Count > 0 && result[^1].End + 1 >= firstPiece)
                result[^1] = new PieceRange(result[^1].Start, Math.Max(result[^1].End, lastPiece));
            else
                result.Add(new PieceRange(firstPiece, lastPiece));
        }

        return result;
    }

    private static int CountPiecesInRanges(List<PieceRange> ranges)
    {
        var count = 0;
        foreach (var range in ranges)
            count += range.End - range.Start + 1;

        return count;
    }

    private static bool IsPieceInRanges(int piece, List<PieceRange> ranges)
    {
        var left = 0;
        var right = ranges.Count - 1;

        while (left <= right)
        {
            var middle = left + (right - left) / 2;
            var range = ranges[middle];

            if (piece < range.Start)
                right = middle - 1;
            else if (piece > range.End)
                left = middle + 1;
            else
                return true;
        }

        return false;
    }

    private readonly record struct PieceRange(int Start, int End);
}
