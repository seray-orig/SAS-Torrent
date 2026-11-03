using System.Net;
using System.Net.Sockets;
using System.Text;
using SAS_Torrent.Console.Bencode;
using SAS_Torrent.Console.Tracker;

namespace SAS_Torrent.Console.Dht;

public sealed class DhtClient
{
    private readonly byte[] _infoHash;
    private readonly string _ourPeerId;
    private readonly int _port;
    private static readonly IPEndPoint[] Bootstrap = new[]
    {
        new IPEndPoint(IPAddress.Parse("67.215.246.10"), 6881),
        new IPEndPoint(IPAddress.Parse("82.221.103.244"), 6881),
        new IPEndPoint(IPAddress.Parse("23.21.224.150"), 6881),
    };

    public DhtClient(byte[] infoHash, string ourPeerId, int port)
    {
        _infoHash = infoHash;
        _ourPeerId = ourPeerId;
        _port = port;
    }

    public async Task<List<PeerInfo>> FindPeersAsync(CancellationToken ct)
    {
        var peers = new List<PeerInfo>();
        try
        {
            using var udp = new UdpClient(0);
            var tid = Random.Shared.Next(10000);
            var req = BuildGetPeers(tid);
            foreach (var b in Bootstrap)
            {
                try { await udp.SendAsync(req, req.Length, b); } catch { }
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    var res = await udp.ReceiveAsync(cts.Token);
                    var dict = BencodeParser.Parse(res.Buffer) as BencodeDictionary;
                    if (dict == null) continue;
                    if (dict.Entries.TryGetValue("r", out var r) && r is BencodeDictionary rd)
                    {
                        if (rd.Entries.TryGetValue("values", out var values) && values is BencodeList vl)
                        {
                            foreach (var v in vl.Items)
                            {
                                if (v is BencodeString vs && vs.Bytes.Length == 6)
                                {
                                    var d = vs.Bytes;
                                    var ip = $"{d[0]}.{d[1]}.{d[2]}.{d[3]}";
                                    var p = (d[4] << 8) | d[5];
                                    peers.Add(new PeerInfo(ip, p));
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
        catch { }
        return peers.DistinctBy(p => p.Ip + ":" + p.Port).ToList();
    }

    private byte[] BuildGetPeers(int tid)
    {
        var d = new BencodeDictionary();
        d.Entries["t"] = new BencodeString(Encoding.ASCII.GetBytes(tid.ToString()));
        d.Entries["y"] = new BencodeString(Encoding.ASCII.GetBytes("q"));
        d.Entries["q"] = new BencodeString(Encoding.ASCII.GetBytes("get_peers"));
        var a = new BencodeDictionary();
        a.Entries["id"] = new BencodeString(RandomId());
        a.Entries["info_hash"] = new BencodeString(_infoHash);
        a.Entries["port"] = new BencodeInt(_port);
        d.Entries["a"] = a;
        return BencodeSerializer.Serialize(d);
    }

    private static byte[] RandomId()
    {
        var b = new byte[20];
        Random.Shared.NextBytes(b);
        return b;
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
