using System.Security.Cryptography;
using System.Text;
using SAS_Torrent.Console.Bencode;

namespace SAS_Torrent.Console.Torrent;

public sealed class TorrentInfo
{
    public byte[] InfoHash { get; }
    public byte[] InfoHashUrlEncoded { get; }
    public string Announce { get; }
    public List<string> AnnounceList { get; } = new();
    public long PieceLength { get; }
    public byte[] Pieces { get; }
    public string Name { get; }
    public long TotalLength { get; }
    public bool IsMultiFile { get; }
    public List<TorrentFile> Files { get; } = new();

    public TorrentInfo(BencodeDictionary root)
    {
        if (root.Entries.TryGetValue("announce", out var announceVal) && announceVal is BencodeString announceStr)
            Announce = announceStr.Text;
        else
            throw new BencodeException("Missing announce in torrent");

        if (root.Entries.TryGetValue("announce-list", out var announceListVal) && announceListVal is BencodeList announceList)
        {
            foreach (var tier in announceList.Items)
            {
                if (tier is BencodeList tierList)
                {
                    foreach (var url in tierList.Items)
                    {
                        if (url is BencodeString urlStr)
                            AnnounceList.Add(urlStr.Text);
                    }
                }
            }
        }

        if (!root.Entries.TryGetValue("info", out var infoVal) || infoVal is not BencodeDictionary infoDict)
            throw new BencodeException("Missing info in torrent");

        InfoHash = ComputeInfoHash(infoDict);
        InfoHashUrlEncoded = UrlEncodeBytes(InfoHash);

        if (infoDict.Entries.TryGetValue("piece length", out var pieceLenVal) && pieceLenVal is BencodeInt pieceLenInt)
            PieceLength = pieceLenInt.Value;
        else
            throw new BencodeException("Missing piece length");

        if (infoDict.Entries.TryGetValue("pieces", out var piecesVal) && piecesVal is BencodeString piecesStr)
            Pieces = piecesStr.Bytes;
        else
            throw new BencodeException("Missing pieces");

        if (infoDict.Entries.TryGetValue("name", out var nameVal) && nameVal is BencodeString nameStr)
            Name = nameStr.Text;
        else
            throw new BencodeException("Missing name");

        if (infoDict.Entries.TryGetValue("length", out var lengthVal) && lengthVal is BencodeInt lengthInt)
        {
            IsMultiFile = false;
            TotalLength = lengthInt.Value;
            Files.Add(new TorrentFile(Name, TotalLength));
        }
        else if (infoDict.Entries.TryGetValue("files", out var filesVal) && filesVal is BencodeList filesList)
        {
            IsMultiFile = true;
            foreach (var f in filesList.Items)
            {
                if (f is not BencodeDictionary fDict) continue;
                if (!fDict.Entries.TryGetValue("length", out var fl) || fl is not BencodeInt flInt) continue;
                if (!fDict.Entries.TryGetValue("path", out var pathVal) || pathVal is not BencodeList pathList) continue;
                var parts = pathList.Items.OfType<BencodeString>().Select(p => p.Text).ToList();
                var relPath = Path.Combine(parts.ToArray());
                Files.Add(new TorrentFile(relPath, flInt.Value));
                TotalLength += flInt.Value;
            }
        }
        else
        {
            throw new BencodeException("Invalid info structure");
        }
    }

    private static byte[] ComputeInfoHash(BencodeDictionary infoDict)
    {
        var raw = GetRawInfo(infoDict);
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(raw);
    }

    private static byte[] GetRawInfo(BencodeDictionary infoDict)
    {
        var stream = new MemoryStream();
        WriteValue(stream, infoDict);
        return stream.ToArray();
    }

    private static void WriteValue(Stream stream, BencodeValue value)
    {
        if (value is BencodeString s)
        {
            var lenBytes = Encoding.ASCII.GetBytes(s.Bytes.Length.ToString());
            stream.Write(lenBytes);
            stream.WriteByte((byte)':');
            stream.Write(s.Bytes);
            return;
        }
        if (value is BencodeInt i)
        {
            var bytes = Encoding.ASCII.GetBytes($"i{i.Value}e");
            stream.Write(bytes);
            return;
        }
        if (value is BencodeList l)
        {
            stream.WriteByte((byte)'l');
            foreach (var item in l.Items) WriteValue(stream, item);
            stream.WriteByte((byte)'e');
            return;
        }
        if (value is BencodeDictionary d)
        {
            stream.WriteByte((byte)'d');
            foreach (var kv in d.Entries.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var keyBytes = Encoding.UTF8.GetBytes(kv.Key);
                var lenBytes = Encoding.ASCII.GetBytes(keyBytes.Length.ToString());
                stream.Write(lenBytes);
                stream.WriteByte((byte)':');
                stream.Write(keyBytes);
                WriteValue(stream, kv.Value);
            }
            stream.WriteByte((byte)'e');
            return;
        }
        throw new BencodeException("Unknown value type");
    }

    private static byte[] UrlEncodeBytes(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if ((b >= 48 && b <= 57) || (b >= 65 && b <= 90) || (b >= 97 && b <= 122) || b == 45 || b == 95 || b == 46 || b == 126)
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}

public sealed class TorrentFile
{
    public string RelativePath { get; }
    public long Length { get; }

    public TorrentFile(string relativePath, long length)
    {
        RelativePath = relativePath;
        Length = length;
    }
}
