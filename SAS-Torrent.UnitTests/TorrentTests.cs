using SAS_Torrent.Console.Bencode;
using SAS_Torrent.Console.Torrent;
using System.Text;

namespace SAS_Torrent.UnitTests;

public class TorrentTests
{
    [Fact]
    public void InfoHashCalculation_Deterministic()
    {
        var infoDict = new BencodeDictionary();
        infoDict.Entries["name"] = new BencodeString(Encoding.UTF8.GetBytes("test"));
        infoDict.Entries["piece length"] = new BencodeInt(16384);
        var pieces = new byte[20];
        for (int i = 0; i < 20; i++) pieces[i] = (byte)(i + 1);
        infoDict.Entries["pieces"] = new BencodeString(pieces);
        infoDict.Entries["length"] = new BencodeInt(123);

        var rootDict = new BencodeDictionary();
        rootDict.Entries["announce"] = new BencodeString(Encoding.ASCII.GetBytes("http://tracker.test/announce"));
        rootDict.Entries["info"] = infoDict;
        var torrent = new TorrentInfo(rootDict);

        Assert.NotNull(torrent.InfoHash);
        Assert.Equal(20, torrent.InfoHash.Length);
    }
}
