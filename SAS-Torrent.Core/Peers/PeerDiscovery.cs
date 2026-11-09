using System.Threading.Channels;
using SAS_Torrent.Core.Dht;
using SAS_Torrent.Core.Logging;
using SAS_Torrent.Core.Torrent;
using SAS_Torrent.Core.Tracker;

namespace SAS_Torrent.Core.Peers;

public sealed class PeerDiscovery
{
    private readonly TorrentInfo _torrent;
    private readonly string _peerId;
    private readonly int _port;
    private readonly ITorrentLogger? _logger;

    public PeerDiscovery(TorrentInfo torrent, string peerId, int port, ITorrentLogger? logger = null)
    {
        _torrent = torrent;
        _peerId = peerId;
        _port = port;
        _logger = logger;
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
                var response = await AnnounceTrackerAsync(trackerUrl, stopped: false, ct);

                if (!string.IsNullOrEmpty(response.FailureReason))
                {
                    _logger?.Log($"[Discovery] Трекер {trackerUrl}: {response.FailureReason}");
                    return;
                }

                _logger?.Log($"[Discovery] Трекер {trackerUrl}: {response.Peers.Count} пиров");
                await EmitAsync(response.Peers);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.Log($"[Discovery] Трекер {trackerUrl} не сработал: {ex.Message}");
            }
        }

        async Task SearchViaDhtAsync()
        {
            try
            {
                await foreach (var peer in new DhtClient(_torrent.InfoHash, _logger).FindPeersAsync(ct))
                    await EmitAsync(new[] { peer });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.Log($"[Discovery] DHT не сработал: {ex.Message}");
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

    public async Task AnnounceStoppedAsync(CancellationToken ct)
    {
        var tasks = BuildTrackerList().Select(async trackerUrl =>
        {
            try
            {
                await AnnounceTrackerAsync(trackerUrl, stopped: true, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.Log($"[Discovery] Трекер {trackerUrl} не подтвердил остановку: {ex.Message}");
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<TrackerResponse> AnnounceTrackerAsync(string trackerUrl, bool stopped, CancellationToken ct)
    {
        var uri = new Uri(trackerUrl);
        if (uri.Scheme == "udp")
        {
            var udpClient = new UdpTrackerClient(_torrent, _peerId, _port, uri);
            return stopped
                ? await udpClient.AnnounceStoppedAsync(ct)
                : await udpClient.AnnounceAsync(ct);
        }

        if (uri.Scheme == "http" || uri.Scheme == "https")
        {
            var httpClient = new TrackerClient(_torrent, _peerId, _port, trackerUrl);
            return stopped
                ? await httpClient.AnnounceAsync("stopped", ct)
                : await httpClient.AnnounceAsync(ct);
        }

        throw new NotSupportedException($"Неподдерживаемая схема трекера: {uri.Scheme}");
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
