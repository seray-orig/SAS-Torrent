namespace SAS_Torrent.Core.Peers.Transport;

public interface IPeerTransport
{
    string Name { get; }

    Task<PeerTransportConnection?> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct);
}
