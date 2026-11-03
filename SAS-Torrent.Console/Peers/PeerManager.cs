using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Peers;

public sealed class PeerManager
{
    private readonly byte[] _infoHash;
    private readonly string _ourPeerId;
    private readonly int _pieceCount;
    private readonly List<PeerConnection> _connections = new();
    private readonly object _lock = new();

    public PeerManager(byte[] infoHash, string ourPeerId, int pieceCount)
    {
        _infoHash = infoHash;
        _ourPeerId = ourPeerId;
        _pieceCount = pieceCount;
    }

    public async Task ConnectToPeersAsync(IEnumerable<Tracker.PeerInfo> peers, CancellationToken ct)
    {
        foreach (var peer in peers)
        {
            if (ct.IsCancellationRequested) break;
            var conn = new PeerConnection(peer.Ip, peer.Port, _infoHash, _ourPeerId) { PieceCount = _pieceCount };
            conn.OnDisconnected += c => RemoveConnection(c);
            var ok = await conn.ConnectAndHandshakeAsync(ct);
            if (!ok)
            {
                conn.Dispose();
                continue;
            }
            await conn.SendAsync(PeerWireMessages.BuildInterested(), ct);
            _ = conn.StartReadingAsync(ct);
            lock (_lock) _connections.Add(conn);
            await Task.Delay(500, ct);
        }
    }

    public List<PeerConnection> GetSeeders()
    {
        lock (_lock)
        {
            return _connections.Where(c => c.IsConnected && c.GotBitfield && c.IsSeeder).ToList();
        }
    }

    public List<PeerConnection> GetAllConnected()
    {
        lock (_lock)
        {
            return _connections.Where(c => c.IsConnected).ToList();
        }
    }

    private void RemoveConnection(PeerConnection conn)
    {
        lock (_lock)
        {
            _connections.Remove(conn);
        }
        try { conn.Dispose(); } catch { }
    }

    public void DisconnectAll()
    {
        List<PeerConnection> list;
        lock (_lock)
        {
            list = _connections.ToList();
            _connections.Clear();
        }
        foreach (var c in list)
        {
            try { c.Disconnect(); c.Dispose(); } catch { }
        }
    }
}
