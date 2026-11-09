using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.Logging;
using SAS_Torrent.Core.Tracker;

namespace SAS_Torrent.Core.Dht;

public sealed class DhtClient
{
    private const int Alpha = 3;
    private const int MaxQueries = 200;
    private const int MaxKnownNodes = 600;
    private const int BootstrapPort = 6881;

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(25);

    private readonly byte[] _infoHash;
    private readonly byte[] _nodeId;
    private readonly IReadOnlyList<string> _bootstrapHosts;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<BencodeDictionary?>> _transactions = new();
    private int _nextTransaction;
    private readonly ITorrentLogger? _logger;

    public DhtClient(byte[] infoHash, IReadOnlyList<string> bootstrapHosts, ITorrentLogger? logger = null)
    {
        _infoHash = infoHash;
        _nodeId = RandomId();
        _bootstrapHosts = bootstrapHosts;
        _logger = logger;
    }

    public async IAsyncEnumerable<PeerInfo> FindPeersAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var known = new Dictionary<string, DhtNode>(StringComparer.Ordinal);
        var nodeLock = new object();

        using var lookupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lookupCts.CancelAfter(LookupTimeout);

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var receiveTask = ReceiveLoopAsync(udp, lookupCts.Token);

        var queries = 0;
        try
        {
            var bootstrapEndpoints = await ResolveBootstrapAsync(lookupCts.Token);
            lock (nodeLock)
            {
                foreach (var endpoint in bootstrapEndpoints)
                    known[endpoint.ToString()] = new DhtNode(endpoint) { IsBootstrap = true };
            }

            while (!lookupCts.IsCancellationRequested && queries < MaxQueries)
            {
                List<DhtNode> batch;
                lock (nodeLock)
                {
                    batch = known.Values
                        .Where(node => !node.Queried && (node.Id != null || node.IsBootstrap))
                        .OrderBy(node => node.DistanceTo(_infoHash))
                        .Take(Alpha)
                        .ToList();

                    foreach (var node in batch)
                        node.Queried = true;
                }

                if (batch.Count == 0) break;
                queries += batch.Count;

                var responses = await Task.WhenAll(batch.Select(node => QueryAsync(
                    udp,
                    node.EndPoint,
                    "get_peers",
                    args => args.Entries["info_hash"] = new BencodeString(_infoHash),
                    lookupCts.Token)));

                var found = new List<PeerInfo>();
                lock (nodeLock)
                {
                    for (var i = 0; i < batch.Count; i++)
                    {
                        var response = responses[i];
                        if (response == null) continue;
                        batch[i].Responded = true;
                        if (response.Entries.TryGetValue("id", out var idValue) && idValue is BencodeString idString &&
                            idString.Bytes.Length == 20)
                            batch[i].Id = idString.Bytes;

                        CollectPeers(response, found, seen);
                        CollectNodes(response, known);
                    }
                }

                foreach (var peer in found)
                    yield return peer;
            }

            _logger?.Log($"[DHT] Опрошено узлов: {queries}, известно узлов: {known.Count}, найдено пиров: {seen.Count}");
        }
        finally
        {
            lookupCts.Cancel();
            try { await receiveTask; } catch { }
        }
    }

    private async Task<BencodeDictionary?> QueryAsync(
        UdpClient udp,
        IPEndPoint endpoint,
        string query,
        Action<BencodeDictionary> fillArgs,
        CancellationToken ct)
    {
        var transactionId = NextTransactionId();
        var completion = new TaskCompletionSource<BencodeDictionary?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _transactions[transactionId] = completion;

        var message = new BencodeDictionary();
        message.Entries["t"] = new BencodeString(new[] { (byte)(transactionId >> 8), (byte)transactionId });
        message.Entries["y"] = new BencodeString(Encoding.ASCII.GetBytes("q"));
        message.Entries["q"] = new BencodeString(Encoding.ASCII.GetBytes(query));

        var args = new BencodeDictionary();
        args.Entries["id"] = new BencodeString(_nodeId);
        fillArgs(args);
        message.Entries["a"] = args;

        try
        {
            await udp.SendAsync(BencodeSerializer.Serialize(message), endpoint, ct);
            return await completion.Task.WaitAsync(QueryTimeout, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or SocketException)
        {
            return null;
        }
        finally
        {
            _transactions.TryRemove(transactionId, out _);
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                if (BencodeParser.Parse(result.Buffer) is not BencodeDictionary response) continue;
                if (!response.Entries.TryGetValue("t", out var transactionValue) ||
                    transactionValue is not BencodeString transactionString ||
                    transactionString.Bytes.Length < 2)
                    continue;

                var transactionId = (transactionString.Bytes[0] << 8) | transactionString.Bytes[1];
                if (!_transactions.TryGetValue(transactionId, out var completion)) continue;

                BencodeDictionary? reply = null;
                if (response.Entries.TryGetValue("y", out var typeValue) &&
                    typeValue is BencodeString typeString && typeString.Text == "r" &&
                    response.Entries.TryGetValue("r", out var replyValue) &&
                    replyValue is BencodeDictionary replyDict)
                    reply = replyDict;

                completion.TrySetResult(reply);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch { }
        }
    }

    private static void CollectPeers(BencodeDictionary response, List<PeerInfo> peers, HashSet<string> seen)
    {
        if (!response.Entries.TryGetValue("values", out var valuesValue) || valuesValue is not BencodeList values)
            return;

        foreach (var value in values.Items)
        {
            if (value is not BencodeString peerString)
                continue;

            if (peerString.Bytes.Length == 6)
            {
                AddPeer(peerString.Bytes, peers, seen);
            }
            else if (peerString.Bytes.Length == 18)
            {
                for (var i = 0; i + 18 <= peerString.Bytes.Length; i += 18)
                    AddPeer(peerString.Bytes.AsSpan(i, 18), peers, seen);
            }
        }
    }

    private static void AddPeer(ReadOnlySpan<byte> bytes, List<PeerInfo> peers, HashSet<string> seen)
    {
        if (bytes.Length != 6 && bytes.Length != 18) return;

        string ip;
        int port;
        if (bytes.Length == 6)
        {
            ip = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
            port = (bytes[4] << 8) | bytes[5];
        }
        else
        {
            ip = new IPAddress(bytes[..16]).ToString();
            port = (bytes[16] << 8) | bytes[17];
        }

        if (!IsValidEndpoint(ip, port)) return;
        var key = $"{ip}:{port}";
        if (seen.Add(key))
            peers.Add(new PeerInfo(ip, port));
    }

    private static void CollectNodes(BencodeDictionary response, Dictionary<string, DhtNode> known)
    {
        if (known.Count >= MaxKnownNodes) return;
        if (!response.Entries.TryGetValue("nodes", out var nodesValue) || nodesValue is not BencodeString nodesString)
            return;

        var data = nodesString.Bytes;
        for (var i = 0; i + 26 <= data.Length; i += 26)
        {
            var ip = $"{data[i + 20]}.{data[i + 21]}.{data[i + 22]}.{data[i + 23]}";
            var port = (data[i + 24] << 8) | data[i + 25];
            if (!IsValidEndpoint(ip, port)) continue;
            if (!IPAddress.TryParse(ip, out var address)) continue;

            var endpoint = new IPEndPoint(address, port);
            var key = endpoint.ToString();
            if (known.ContainsKey(key)) continue;

            known[key] = new DhtNode(endpoint) { Id = data.AsSpan(i, 20).ToArray() };
            if (known.Count >= MaxKnownNodes) return;
        }
    }

    private async Task<List<IPEndPoint>> ResolveBootstrapAsync(CancellationToken ct)
    {
        var tasks = _bootstrapHosts.Select(async host =>
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                return addresses
                    .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new IPEndPoint(address, BootstrapPort))
                    .ToList();
            }
            catch
            {
                return new List<IPEndPoint>();
            }
        });

        var resolved = await Task.WhenAll(tasks);
        return resolved
            .SelectMany(endpoints => endpoints)
            .DistinctBy(endpoint => endpoint.ToString())
            .ToList();
    }

    private int NextTransactionId()
    {
        while (true)
        {
            var transactionId = Interlocked.Increment(ref _nextTransaction) & 0xFFFF;
            if (!_transactions.ContainsKey(transactionId))
                return transactionId;
        }
    }

    private static bool IsValidEndpoint(string ip, int port)
    {
        if (port <= 0 || port > 65535) return false;
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(address)) return false;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)) return false;
        var bytes = address.GetAddressBytes();
        if (bytes[0] >= 224) return false;
        return true;
    }

    private static byte[] RandomId()
    {
        var bytes = new byte[20];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private sealed class DhtNode
    {
        public DhtNode(IPEndPoint endPoint)
        {
            EndPoint = endPoint;
        }

        public IPEndPoint EndPoint { get; }
        public byte[]? Id { get; set; }
        public bool Queried { get; set; }
        public bool Responded { get; set; }
        public bool IsBootstrap { get; init; }

        public BigInteger DistanceTo(byte[] target)
        {
            if (Id == null) return BigInteger.One << 200;
            var distance = new byte[Math.Min(Id.Length, target.Length)];
            for (var i = 0; i < distance.Length; i++)
                distance[i] = (byte)(Id[i] ^ target[i]);
            return new BigInteger(distance, isUnsigned: true, isBigEndian: true);
        }
    }
}

public static class BencodeSerializer
{
    public static byte[] Serialize(BencodeValue v)
    {
        using var ms = new MemoryStream();
        Write(v, ms);
        return ms.ToArray();
    }

    private static void Write(BencodeValue v, Stream s)
    {
        if (v is BencodeString bs)
        {
            var len = Encoding.ASCII.GetBytes(bs.Bytes.Length.ToString());
            s.Write(len);
            s.WriteByte((byte)':');
            s.Write(bs.Bytes);
            return;
        }
        if (v is BencodeInt bi)
        {
            var bytes = Encoding.ASCII.GetBytes($"i{bi.Value}e");
            s.Write(bytes);
            return;
        }
        if (v is BencodeList bl)
        {
            s.WriteByte((byte)'l');
            foreach (var i in bl.Items) Write(i, s);
            s.WriteByte((byte)'e');
            return;
        }
        if (v is BencodeDictionary bd)
        {
            s.WriteByte((byte)'d');
            foreach (var kv in bd.Entries.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var kb = Encoding.UTF8.GetBytes(kv.Key);
                var kl = Encoding.ASCII.GetBytes(kb.Length.ToString());
                s.Write(kl);
                s.WriteByte((byte)':');
                s.Write(kb);
                Write(kv.Value, s);
            }
            s.WriteByte((byte)'e');
            return;
        }
    }
}
