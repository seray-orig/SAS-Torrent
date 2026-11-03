using SAS_Torrent.Console.Peers.Transport;
using SAS_Torrent.Console.Tracker;

namespace SAS_Torrent.Console.Peers;

public sealed class PeerManager
{
    private const int DefaultMaxParallelConnects = 64;

    private static readonly IReadOnlyList<IPeerTransport> Transports = new IPeerTransport[]
    {
        new TcpPeerTransport(),
        new UtpPeerTransport()
    };

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

    public async Task ConnectToPeersAsync(
        IAsyncEnumerable<PeerInfo> peers,
        CancellationToken ct,
        int maxParallel = DefaultMaxParallelConnects)
    {
        using var semaphore = new SemaphoreSlim(maxParallel);
        var pending = new List<Task>();

        try
        {
            await foreach (var peer in peers.WithCancellation(ct))
            {
                await semaphore.WaitAsync(ct);
                pending.Add(ConnectToPeerDetachedAsync(peer, semaphore, ct));
                pending.RemoveAll(t => t.IsCompleted);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await Task.WhenAll(pending); } catch { }
        }
    }

    private async Task ConnectToPeerDetachedAsync(PeerInfo peer, SemaphoreSlim semaphore, CancellationToken ct)
    {
        try
        {
            await ConnectToPeerAsync(peer, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[Peer {peer.Ip}:{peer.Port}] Ошибка подключения: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<bool> ConnectToPeerAsync(PeerInfo peer, CancellationToken ct)
    {
        var conn = new PeerConnection(peer.Ip, peer.Port, _infoHash, _ourPeerId, Transports) { PieceCount = _pieceCount };
        conn.OnDisconnected += RemoveConnection;

        if (!await conn.ConnectAndHandshakeAsync(ct))
        {
            conn.Dispose();
            return false;
        }

        await conn.SendAsync(PeerWireMessages.BuildInterested(), ct);
        _ = conn.StartReadingAsync(ct);

        lock (_lock)
            _connections.Add(conn);

        System.Console.WriteLine($"[Peer {peer.Ip}:{peer.Port}] Подключён, ожидаем битфилд");
        return true;
    }

    public List<PeerConnection> GetSeeders()
    {
        lock (_lock)
        {
            return _connections.Where(c => c.IsConnected && c.IsSeeder).ToList();
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
