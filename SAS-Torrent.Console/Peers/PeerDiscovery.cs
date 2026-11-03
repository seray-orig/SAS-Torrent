using System.Threading.Channels;
using SAS_Torrent.Console.Dht;
using SAS_Torrent.Console.Torrent;
using SAS_Torrent.Console.Tracker;

namespace SAS_Torrent.Console.Peers;

public sealed class PeerDiscovery
{
    private readonly TorrentInfo _torrent;
    private readonly string _peerId;
    private readonly int _port;

    public PeerDiscovery(TorrentInfo torrent, string peerId, int port)
    {
        _torrent = torrent;
        _peerId = peerId;
        _port = port;
    }

    public IAsyncEnumerable<PeerInfo> DiscoverAsync(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<PeerInfo>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _ = Task.Run(() => RunProducersAsync(channel.Writer, ct), CancellationToken.None);
        return channel.Reader.ReadAllAsync(ct);
    }

    private async Task RunProducersAsync(ChannelWriter<PeerInfo> writer, CancellationToken ct)
    {
        var seen = new HashSet<string>();
        var gate = new object();

        async Task EmitAsync(IEnumerable<PeerInfo> peers)
        {
            foreach (var peer in peers)
            {
                if (ct.IsCancellationRequested) return;
                bool isNew;
                lock (gate)
                    isNew = seen.Add($"{peer.Ip}:{peer.Port}");
                if (!isNew) continue;

                try { await writer.WriteAsync(peer, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        async Task AnnounceViaTrackerAsync(string trackerUrl)
        {
            try
            {
                var uri = new Uri(trackerUrl);
                TrackerResponse response;
                if (uri.Scheme == "udp")
                    response = await new UdpTrackerClient(_torrent, _peerId, _port, uri).AnnounceAsync(ct);
                else if (uri.Scheme == "http" || uri.Scheme == "https")
                    response = await new TrackerClient(_torrent, _peerId, _port, trackerUrl).AnnounceAsync(ct);
                else
                    return;

                if (!string.IsNullOrEmpty(response.FailureReason))
                {
                    System.Console.WriteLine($"[Discovery] Трекер {trackerUrl}: {response.FailureReason}");
                    return;
                }

                System.Console.WriteLine($"[Discovery] Трекер {trackerUrl}: {response.Peers.Count} пиров");
                await EmitAsync(response.Peers);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Discovery] Трекер {trackerUrl} не сработал: {ex.Message}");
            }
        }

        async Task SearchViaDhtAsync()
        {
            try
            {
                var peers = await new DhtClient(_torrent.InfoHash, _peerId, _port).FindPeersAsync(ct);
                System.Console.WriteLine($"[Discovery] DHT: {peers.Count} пиров");
                await EmitAsync(peers);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Discovery] DHT не сработал: {ex.Message}");
            }
        }

        var tasks = BuildTrackerList().Select(AnnounceViaTrackerAsync).ToList();
        tasks.Add(SearchViaDhtAsync());

        try
        {
            await Task.WhenAll(tasks);
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private List<string> BuildTrackerList()
    {
        var trackers = new List<string>();
        if (!string.IsNullOrEmpty(_torrent.Announce))
            trackers.Add(_torrent.Announce);
        foreach (var tracker in _torrent.AnnounceList)
            if (!trackers.Contains(tracker))
                trackers.Add(tracker);
        return trackers;
    }
}
