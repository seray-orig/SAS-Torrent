using System.Net;
using System.Text;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Core.Tracker;

public sealed class TrackerClient
{
    private readonly TorrentInfo _torrent;
    private readonly string _peerId;
    private readonly int _port;
    private readonly string _userAgent;
    private readonly string _announceUrl;

    public TrackerClient(TorrentInfo torrent, string peerId, int port, string userAgent, string? announceUrl = null)
    {
        _torrent = torrent;
        _peerId = peerId;
        _port = port;
        _userAgent = userAgent;
        _announceUrl = announceUrl ?? torrent.Announce;
    }

    public Task<TrackerResponse> AnnounceAsync(CancellationToken ct)
    {
        return AnnounceAsync("started", ct);
    }

    public async Task<TrackerResponse> AnnounceAsync(string? eventName, CancellationToken ct)
    {
        var url = BuildUrl(_announceUrl, eventName);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        using var http = new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var dict = BencodeParser.Parse(bytes) as BencodeDictionary;
        if (dict == null) throw new BencodeException("Tracker response is not a dictionary");
        return new TrackerResponse(dict);
    }

    private string BuildUrl(string announceUrl, string? eventName)
    {
        var peerIdBytes = Encoding.ASCII.GetBytes(_peerId);
        var infoHash = _torrent.InfoHashUrlEncoded;
        var peerIdEnc = UrlEncode(peerIdBytes);

        var sb = new StringBuilder();
        sb.Append(announceUrl);
        sb.Append(announceUrl.Contains('?') ? '&' : '?');
        sb.Append("info_hash=").Append(Encoding.ASCII.GetString(infoHash));
        sb.Append("&peer_id=").Append(peerIdEnc);
        sb.Append("&port=").Append(_port);
        sb.Append("&uploaded=0&downloaded=0&left=").Append(_torrent.TotalLength);
        sb.Append("&compact=1&no_peer_id=1");
        if (!string.IsNullOrEmpty(eventName))
            sb.Append("&event=").Append(eventName);
        return sb.ToString();
    }

    private static string UrlEncode(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if ((b >= 48 && b <= 57) || (b >= 65 && b <= 90) || (b >= 97 && b <= 122) || b == 45 || b == 95 || b == 46 || b == 126)
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
