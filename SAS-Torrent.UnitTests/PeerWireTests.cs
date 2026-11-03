using SAS_Torrent.Console.Peers;

namespace SAS_Torrent.UnitTests;

public class PeerWireTests
{
    [Fact]
    public void BuildHandshake_HasCorrectFormat()
    {
        var infoHash = new byte[20];
        for (int i = 0; i < 20; i++) infoHash[i] = (byte)i;
        var peerId = "-ST0001-123456789012";
        var hs = PeerWireMessages.BuildHandshake(infoHash, peerId);
        Assert.Equal(68, hs.Length);
        Assert.Equal(19, hs[0]);
    }

    [Fact]
    public void BuildRequest_HasCorrectFormat()
    {
        var req = PeerWireMessages.BuildRequest(0, 0, 16384);
        Assert.Equal(17, req.Length);
        Assert.Equal((byte)PeerMessageId.Request, req[4]);
    }

    [Fact]
    public void BuildInterested_HasCorrectFormat()
    {
        var msg = PeerWireMessages.BuildInterested();
        Assert.Equal(5, msg.Length);
        Assert.Equal((byte)PeerMessageId.Interested, msg[4]);
    }
}
