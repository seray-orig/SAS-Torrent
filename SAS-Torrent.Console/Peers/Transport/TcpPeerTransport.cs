using System.Net.Sockets;

namespace SAS_Torrent.Console.Peers.Transport;

public sealed class TcpPeerTransport : IPeerTransport
{
    public string Name => "TCP";

    public async Task<PeerTransportConnection?> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            await client.ConnectAsync(ip, port, cts.Token);
            client.NoDelay = true;

            return new PeerTransportConnection(client.GetStream(), Name, client);
        }
        catch
        {
            client.Dispose();
            return null;
        }
    }
}
