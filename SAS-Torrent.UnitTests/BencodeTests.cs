using SAS_Torrent.Core.Bencode;
using System.Text;

namespace SAS_Torrent.UnitTests;

public class BencodeTests
{
    [Fact]
    public void ParseString()
    {
        var data = Encoding.ASCII.GetBytes("4:spam");
        var val = BencodeParser.Parse(data) as BencodeString;
        Assert.NotNull(val);
        Assert.Equal("spam", val.Text);
    }

    [Fact]
    public void ParseInt()
    {
        var data = Encoding.ASCII.GetBytes("i42e");
        var val = BencodeParser.Parse(data) as BencodeInt;
        Assert.NotNull(val);
        Assert.Equal(42, val.Value);
    }

    [Fact]
    public void ParseList()
    {
        var data = Encoding.ASCII.GetBytes("l4:spami42ee");
        var val = BencodeParser.Parse(data) as BencodeList;
        Assert.NotNull(val);
        Assert.Equal(2, val.Items.Count);
    }

    [Fact]
    public void ParseDictionary()
    {
        var data = Encoding.ASCII.GetBytes("d3:foo3:bar5:helloi52ee");
        var val = BencodeParser.Parse(data) as BencodeDictionary;
        Assert.NotNull(val);
        Assert.True(val.Entries.ContainsKey("foo"));
        Assert.True(val.Entries.ContainsKey("hello"));
    }
}
