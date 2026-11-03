using SAS_Torrent.Console.Bencode;

namespace SAS_Torrent.Console.Tracker;

public sealed class TrackerResponse
{
    public List<PeerInfo> Peers { get; } = new();
    public string? FailureReason { get; }
    public long Interval { get; } = 1800;

    public TrackerResponse(BencodeDictionary dict)
    {
        if (dict.Entries.TryGetValue("failure reason", out var fail) && fail is BencodeString failStr)
            FailureReason = failStr.Text;

        if (dict.Entries.TryGetValue("interval", out var interval) && interval is BencodeInt intervalInt)
            Interval = intervalInt.Value;

        if (dict.Entries.TryGetValue("peers", out var peersVal))
        {
            if (peersVal is BencodeString peersStr)
            {
                var data = peersStr.Bytes;
                for (int i = 0; i + 6 <= data.Length; i += 6)
                {
                    var ip = $"{data[i]}.{data[i + 1]}.{data[i + 2]}.{data[i + 3]}";
                    var port = (data[i + 4] << 8) | data[i + 5];
                    Peers.Add(new PeerInfo(ip, port));
                }
            }
            else if (peersVal is BencodeList peersList)
            {
                foreach (var p in peersList.Items)
                {
                    if (p is not BencodeDictionary pd) continue;
                    if (!pd.Entries.TryGetValue("ip", out var ipVal) || ipVal is not BencodeString ipStr) continue;
                    if (!pd.Entries.TryGetValue("port", out var portVal) || portVal is not BencodeInt portInt) continue;
                    Peers.Add(new PeerInfo(ipStr.Text, (int)portInt.Value));
                }
            }
        }
    }

    public TrackerResponse(List<PeerInfo> peers, long interval = 1800)
    {
        Peers.AddRange(peers);
        Interval = interval;
    }
}

public sealed class PeerInfo
{
    public string Ip { get; }
    public int Port { get; }

    public PeerInfo(string ip, int port)
    {
        Ip = ip;
        Port = port;
    }
}
