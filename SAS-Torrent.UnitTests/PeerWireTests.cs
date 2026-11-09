using System.IO;
using System.Reflection;
using SAS_Torrent.Core.Peers;

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

    [Fact]
    public void BuildCancel_HasCorrectFormat()
    {
        var msg = PeerWireMessages.BuildCancel(1, 2, 16384);
        Assert.Equal(17, msg.Length);
        Assert.Equal((byte)PeerMessageId.Cancel, msg[4]);
    }

    [Fact]
    public void BuildKeepAlive_HasNoPayload()
    {
        Assert.Equal(new byte[4], PeerWireMessages.BuildKeepAlive());
    }

    [Fact]
    public void OutboundPolicy_AllowsOnlySafeMessages()
    {
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildInterested()));
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildNotInterested()));
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildChoke()));
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildRequest(0, 0, 16384)));
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildCancel(0, 0, 16384)));
        Assert.True(PeerOutboundPolicy.IsAllowed(PeerWireMessages.BuildKeepAlive()));
    }

    [Theory]
    [InlineData(PeerMessageId.Unchoke)]
    [InlineData(PeerMessageId.Have)]
    [InlineData(PeerMessageId.Bitfield)]
    [InlineData(PeerMessageId.Piece)]
    public void OutboundPolicy_RejectsUploadMessages(PeerMessageId id)
    {
        Assert.False(PeerOutboundPolicy.IsAllowed(BuildMessage(id)));
    }

    [Fact]
    public void OutboundPolicy_RejectsMalformedMessage()
    {
        var message = new byte[5];
        message[4] = (byte)PeerMessageId.Interested;

        Assert.False(PeerOutboundPolicy.IsAllowed(message));
    }

    [Fact]
    public void PeerConnection_DoesNotExposeRawByteSender()
    {
        var methods = typeof(PeerConnection).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var byteLikeTypes = new[]
        {
            typeof(byte[]),
            typeof(Memory<byte>),
            typeof(ReadOnlyMemory<byte>),
            typeof(Stream)
        };

        Assert.DoesNotContain(methods, method => method.Name == "SendAsync");
        Assert.DoesNotContain(methods, method =>
            method.GetParameters().Any(parameter => byteLikeTypes.Contains(parameter.ParameterType)));
    }

    [Fact]
    public void PeerConnection_ExposesOnlyP2oSafeSendMethods()
    {
        var sendMethods = typeof(PeerConnection).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Send", StringComparison.Ordinal))
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "SendCancelAsync",
                "SendChokeAsync",
                "SendInterestedAsync",
                "SendKeepAliveAsync",
                "SendRequestAsync"
            },
            sendMethods);
    }

    [Theory]
    [InlineData("BuildPiece")]
    [InlineData("BuildUnchoke")]
    [InlineData("BuildHave")]
    [InlineData("BuildBitfield")]
    public void PeerWireMessages_HasNoUploadBuilders(string methodName)
    {
        Assert.Null(typeof(PeerWireMessages).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static));
    }

    private static byte[] BuildMessage(PeerMessageId id)
    {
        var message = new byte[5];
        message[3] = 1;
        message[4] = (byte)id;
        return message;
    }
}
