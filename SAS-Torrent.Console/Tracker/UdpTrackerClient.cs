using System.Net;
using System.Net.Sockets;
using System.Text;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Tracker;

public sealed class UdpTrackerClient
{
    private readonly TorrentInfo _torrent;
    private readonly string _peerId;
    private readonly int _port;
    private readonly Uri _uri;

    public UdpTrackerClient(TorrentInfo torrent, string peerId, int port, Uri uri)
    {
        _torrent = torrent;
        _peerId = peerId;
        _port = port;
        _uri = uri;
    }

    public async Task<TrackerResponse> AnnounceAsync(CancellationToken ct)
    {
        if (_uri.Port == -1)
            throw new Exception($"UDP трекер без порта: {_uri}");

        var hostEntry = await Dns.GetHostEntryAsync(_uri.Host, ct);
        var addresses = hostEntry.AddressList
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
            .ToArray();

        if (addresses.Length == 0)
            throw new Exception($"Не удалось резолвить хост UDP трекера: {_uri.Host}");

        Exception? last = null;
        foreach (var addr in addresses)
        {
            try
            {
                using var udp = new UdpClient(addr.AddressFamily);
                var endPoint = new IPEndPoint(addr, _uri.Port);
                udp.Client.Connect(endPoint);

                var transactionId = Random.Shared.Next();
                var connectionId = await ConnectAsync(udp, transactionId, ct);
                var announceResp = await AnnounceAsync(udp, connectionId, transactionId, ct);
                return ParseAnnounce(announceResp);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new Exception("Не удалось связаться с UDP трекером");
    }

    private async Task<ulong> ConnectAsync(UdpClient udp, int transactionId, CancellationToken ct)
    {
        var req = new byte[16];
        WriteLong(req, 0, 0x41727101980UL);
        WriteInt(req, 8, 0);
        WriteInt(req, 12, transactionId);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await udp.SendAsync(req, req.Length);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var res = await udp.ReceiveAsync(cts.Token);
                if (res.Buffer.Length < 16) throw new Exception("Неверный ответ connect от UDP трекера");
                var action = ReadInt(res.Buffer, 0);
                var tid = ReadInt(res.Buffer, 4);
                if (action != 0 || tid != transactionId) throw new Exception("Неверный ответ connect от UDP трекера");
                return ReadLong(res.Buffer, 8);
            }
            catch (OperationCanceledException) when (attempt < 3)
            {
                continue;
            }
            catch (SocketException) when (attempt < 3)
            {
                await Task.Delay(1000, ct);
                continue;
            }
        }

        throw new Exception("Таймаут connect к UDP трекеру");
    }

    private async Task<byte[]> AnnounceAsync(UdpClient udp, ulong connectionId, int transactionId, CancellationToken ct)
    {
        var req = new byte[98];
        WriteLong(req, 0, connectionId);
        WriteInt(req, 8, 1);
        WriteInt(req, 12, transactionId);
        Buffer.BlockCopy(_torrent.InfoHash, 0, req, 16, 20);
        var peerIdBytes = Encoding.ASCII.GetBytes(_peerId);
        Buffer.BlockCopy(peerIdBytes, 0, req, 36, 20);
        WriteLong(req, 56, 0UL);
        WriteLong(req, 64, (ulong)_torrent.TotalLength);
        WriteLong(req, 72, 0UL);
        WriteInt(req, 80, 0);
        WriteInt(req, 84, 0);
        WriteInt(req, 88, Random.Shared.Next());
        WriteInt(req, 92, -1);
        WriteShort(req, 96, (ushort)_port);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await udp.SendAsync(req, req.Length);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                var res = await udp.ReceiveAsync(cts.Token);
                if (res.Buffer.Length < 20) throw new Exception("Неверный ответ announce от UDP трекера");
                var action = ReadInt(res.Buffer, 0);
                var tid = ReadInt(res.Buffer, 4);
                if (action != 1 || tid != transactionId) throw new Exception("Неверный ответ announce от UDP трекера");
                return res.Buffer;
            }
            catch (OperationCanceledException) when (attempt < 3)
            {
                continue;
            }
            catch (SocketException) when (attempt < 3)
            {
                await Task.Delay(1000, ct);
                continue;
            }
        }

        throw new Exception("Таймаут announce к UDP трекеру");
    }

    private TrackerResponse ParseAnnounce(byte[] buf)
    {
        var interval = ReadInt(buf, 8);
        var peers = new List<PeerInfo>();
        for (int i = 20; i + 6 <= buf.Length; i += 6)
        {
            var ip = $"{buf[i]}.{buf[i + 1]}.{buf[i + 2]}.{buf[i + 3]}";
            var port = (buf[i + 4] << 8) | buf[i + 5];
            peers.Add(new PeerInfo(ip, port));
        }
        return new TrackerResponse(peers, interval);
    }

    private static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteShort(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)value;
    }

    private static void WriteLong(byte[] buf, int offset, ulong value)
    {
        WriteInt(buf, offset, (int)(value >> 32));
        WriteInt(buf, offset + 4, (int)(value & 0xFFFFFFFFUL));
    }

    private static int ReadInt(byte[] buf, int offset)
    {
        return (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
    }

    private static ulong ReadLong(byte[] buf, int offset)
    {
        var high = (uint)ReadInt(buf, offset);
        var low = (uint)ReadInt(buf, offset + 4);
        return ((ulong)high << 32) | low;
    }
}
