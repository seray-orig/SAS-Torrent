using System.Net;
using System.Net.Sockets;
using SAS_Torrent.Core.Logging;

namespace SAS_Torrent.Core.Peers;

public sealed class PeerInboundListener : IDisposable
{
    private const int MaxRequestsWhileChoked = 3;
    private const int MaxInboundMessageSize = 1024 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly byte[] _infoHash;
    private readonly string _peerId;
    private readonly ITorrentLogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private PeerInboundListener(TcpListener listener, byte[] infoHash, string peerId, ITorrentLogger? logger)
    {
        _listener = listener;
        _infoHash = infoHash;
        _peerId = peerId;
        _logger = logger;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static PeerInboundListener Start(int preferredPort, byte[] infoHash, string peerId, ITorrentLogger? logger = null)
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Any, preferredPort);
            listener.Start();
        }
        catch (SocketException) when (preferredPort != 0)
        {
            listener = new TcpListener(IPAddress.Any, 0);
            listener.Start();
        }

        var result = new PeerInboundListener(listener, infoHash, peerId, logger);
        _ = Task.Run(() => result.AcceptLoopAsync(result._cts.Token));
        logger?.Log($"[Listener] P2O-вход на порту {result.Port}: только handshake, Choke и игнор Request");
        return result;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) return;
                continue;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                _logger?.Log($"[Listener] Ошибка accept: {ex.Message}");
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeCts.CancelAfter(HandshakeTimeout);

                var handshake = new byte[68];
                await ReadExactAsync(stream, handshake, handshakeCts.Token);
                if (!IsValidHandshake(handshake)) return;

                await stream.WriteAsync(PeerWireMessages.BuildHandshake(_infoHash, _peerId), ct);
                await stream.WriteAsync(PeerWireMessages.BuildChoke(), ct);
                await stream.FlushAsync(ct);

                var requests = 0;
                var lengthBuffer = new byte[4];
                while (!ct.IsCancellationRequested)
                {
                    await ReadExactAsync(stream, lengthBuffer, ct);
                    var length = PeerWireMessages.ReadInt(lengthBuffer, 0);
                    if (length <= 0) continue;
                    if (length > MaxInboundMessageSize) return;

                    var message = new byte[length];
                    await ReadExactAsync(stream, message, ct);

                    if ((PeerMessageId)message[0] == PeerMessageId.Request && ++requests >= MaxRequestsWhileChoked)
                        return;
                }
            }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private bool IsValidHandshake(byte[] handshake)
    {
        if (handshake[0] != 19) return false;
        if (!handshake.AsSpan(1, 19).SequenceEqual("BitTorrent protocol"u8)) return false;
        return handshake.AsSpan(28, 20).SequenceEqual(_infoHash);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }
}
