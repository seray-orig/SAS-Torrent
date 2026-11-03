using System.Collections.Concurrent;

namespace SAS_Torrent.Console.Peers;

public sealed class PieceBlock
{
    public int PieceIndex { get; set; }
    public int Begin { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public sealed class PieceAssembler
{
    private readonly int _pieceCount;
    private readonly long _pieceLength;
    private readonly long _totalLength;
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, byte[]>> _pieces = new();
    private readonly HashSet<int> _completed = new();
    private readonly object _lock = new();

    public event Action<int>? OnPieceCompleted;

    public PieceAssembler(int pieceCount, long pieceLength, long totalLength)
    {
        _pieceCount = pieceCount;
        _pieceLength = pieceLength;
        _totalLength = totalLength;
    }

    public void AddBlock(PieceBlock block)
    {
        var pieceBlocks = _pieces.GetOrAdd(block.PieceIndex, _ => new ConcurrentDictionary<int, byte[]>());
        pieceBlocks[block.Begin] = block.Data;
        TryAssemble(block.PieceIndex);
    }

    private void TryAssemble(int pieceIndex)
    {
        if (_completed.Contains(pieceIndex)) return;
        var pieceSize = GetPieceSize(pieceIndex);
        if (!_pieces.TryGetValue(pieceIndex, out var blocks)) return;
        // check all blocks present
        var blockSize = 16384;
        var numBlocks = (int)Math.Ceiling((double)pieceSize / blockSize);
        for (int i = 0; i < numBlocks; i++)
        {
            if (!blocks.ContainsKey(i * blockSize)) return;
        }
        // assemble
        var assembled = new byte[pieceSize];
        for (int i = 0; i < numBlocks; i++)
        {
            var begin = i * blockSize;
            if (blocks.TryGetValue(begin, out var data))
            {
                Array.Copy(data, 0, assembled, begin, data.Length);
            }
        }
        lock (_lock)
        {
            if (_completed.Contains(pieceIndex)) return;
            _completed.Add(pieceIndex);
        }
        OnPieceCompleted?.Invoke(pieceIndex);
        // store assembled temporarily via event - handled externally
    }

    public byte[]? GetAssembled(int pieceIndex)
    {
        var pieceSize = GetPieceSize(pieceIndex);
        if (!_pieces.TryGetValue(pieceIndex, out var blocks)) return null;
        var blockSize = 16384;
        var numBlocks = (int)Math.Ceiling((double)pieceSize / blockSize);
        var assembled = new byte[pieceSize];
        for (int i = 0; i < numBlocks; i++)
        {
            var begin = i * blockSize;
            if (blocks.TryGetValue(begin, out var data))
                Array.Copy(data, 0, assembled, begin, data.Length);
        }
        return assembled;
    }

    private int GetPieceSize(int pieceIndex)
    {
        if (pieceIndex == _pieceCount - 1)
        {
            var last = _totalLength % _pieceLength;
            return last == 0 ? (int)_pieceLength : (int)last;
        }
        return (int)_pieceLength;
    }

    public void ResetPiece(int pieceIndex)
    {
        lock (_lock)
        {
            _completed.Remove(pieceIndex);
        }
        _pieces.TryRemove(pieceIndex, out _);
    }

    public bool IsComplete() => _completed.Count == _pieceCount;
    public int CompletedCount() => _completed.Count;
}
