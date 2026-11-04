using SAS_Torrent.Core.Utp;

namespace SAS_Torrent.Core.Peers.Transport;

public sealed class UtpPeerTransport : IPeerTransport
{
    public string Name => "uTP";

    public async Task<PeerTransportConnection?> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        var stream = await UtpStream.ConnectAsync(ip, port, timeout, ct);
        return stream == null ? null : new PeerTransportConnection(stream, Name, stream);
    }
}
