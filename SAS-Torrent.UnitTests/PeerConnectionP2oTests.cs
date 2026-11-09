using System.Net;
using System.Net.Sockets;
using SAS_Torrent.Core.Peers;
using SAS_Torrent.Core.Peers.Transport;

namespace SAS_Torrent.UnitTests;

public class PeerConnectionP2oTests
{
    private static readonly byte[] InfoHash = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
    private const string OurPeerId = "-ST0001-123456789012";
    private const string RemotePeerId = "-PEER01-000000000000";

    [Fact]
    public async Task PeerConnection_OnRequestFromPeer_NeverSendsPieceOrUnchoke()
    {
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();

        try
        {
            var port = ((IPEndPoint)server.LocalEndpoint).Port;
            var serverTask = Task.Run(async () =>
            {
                using var client = await server.AcceptTcpClientAsync();
                var stream = client.GetStream();

                var handshake = new byte[68];
                await ReadExactAsync(stream, handshake);
                await stream.WriteAsync(PeerWireMessages.BuildHandshake(InfoHash, RemotePeerId));
                await stream.WriteAsync(PeerWireMessages.BuildChoke());

                for (var i = 0; i < 3; i++)
                    await stream.WriteAsync(PeerWireMessages.BuildRequest(0, i * 16384, 16384));

                await stream.FlushAsync();

                var received = new List<byte[]>();
                var buffer = new byte[4096];
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

                try
                {
                    while (true)
                    {
                        var count = await stream.ReadAsync(buffer, readCts.Token);
                        if (count == 0) break;
                        received.Add(buffer[..count]);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }

                return received;
            });

            using var connection = new PeerConnection(
                "127.0.0.1",
                port,
                InfoHash,
                OurPeerId,
                new IPeerTransport[] { new TcpPeerTransport() });

            Assert.True(await connection.ConnectAndHandshakeAsync(CancellationToken.None));

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.OnDisconnected += _ => disconnected.TrySetResult();
            _ = connection.StartReadingAsync(CancellationToken.None);

            var received = await serverTask;
            Assert.True(await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(3))) == disconnected.Task);
            AssertNoUploadMessages(received);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task InboundListener_ChokesAndNeverSendsPieceOrUnchoke()
    {
        using var listener = PeerInboundListener.Start(0, InfoHash, OurPeerId);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);

        var stream = client.GetStream();
        await stream.WriteAsync(PeerWireMessages.BuildHandshake(InfoHash, RemotePeerId));
        await stream.FlushAsync();

        var handshake = new byte[68];
        await ReadExactAsync(stream, handshake);

        var choke = new byte[5];
        await ReadExactAsync(stream, choke);
        Assert.Equal((byte)PeerMessageId.Choke, choke[4]);

        for (var i = 0; i < 3; i++)
            await stream.WriteAsync(PeerWireMessages.BuildRequest(0, i * 16384, 16384));

        await stream.FlushAsync();

        var received = new List<byte[]>();
        var buffer = new byte[4096];
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(buffer, readCts.Token);
                if (count == 0) break;
                received.Add(buffer[..count]);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }

        AssertNoUploadMessages(received);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
    }

    private static void AssertNoUploadMessages(List<byte[]> chunks)
    {
        var data = chunks.SelectMany(chunk => chunk).ToArray();
        var offset = 0;

        while (offset + 4 <= data.Length)
        {
            var length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            offset += 4;
            if (length == 0) continue;
            if (offset + length > data.Length) break;

            var id = (PeerMessageId)data[offset];
            Assert.False(id == PeerMessageId.Piece, "Клиент отправил Piece");
            Assert.False(id == PeerMessageId.Unchoke, "Клиент отправил Unchoke");
            offset += length;
        }
    }
}
