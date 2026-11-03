using System.Net.Sockets;
using System.Security.Cryptography;
using SAS_Torrent.Console.Torrent;

namespace SAS_Torrent.Console.Peers;

public sealed class PeerConnection : IDisposable
{
    private readonly TcpClient _client;
    private Stream? _stream;
    private readonly byte[] _infoHash;
    private readonly string _ourPeerId;
    private bool _disposed;

    public string Ip { get; }
    public int Port { get; }
    public bool IsConnected { get; private set; }
    public bool IsSeeder { get; private set; }
    public bool GotBitfield { get; private set; }
    public bool Choked { get; private set; } = true;
    public bool PeerChoking { get; private set; } = true;
    public int PieceCount { get; set; } = 0;
    private System.Collections.BitArray? _peerPieces;

    public event Action<PeerConnection, byte[]>? OnPiece;
    public event Action<PeerConnection>? OnDisconnected;

    public PeerConnection(string ip, int port, byte[] infoHash, string ourPeerId)
    {
        Ip = ip;
        Port = port;
        _infoHash = infoHash;
        _ourPeerId = ourPeerId;
        _client = new TcpClient();
        _stream = null;
    }

    public async Task<bool> ConnectAndHandshakeAsync(CancellationToken ct)
    {
        try
        {
            System.Console.WriteLine($"[Peer {Ip}:{Port}] Connecting...");
            var connectTask = _client.ConnectAsync(Ip, Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(5000, ct));
            if (completed != connectTask) { System.Console.WriteLine($"[Peer {Ip}:{Port}] Connect timeout"); return false; }
            await connectTask;
            System.Console.WriteLine($"[Peer {Ip}:{Port}] Connected, sending handshake");

            _stream = _client.GetStream();
            _client.Client.NoDelay = true;

            var hs = PeerWireMessages.BuildHandshake(_infoHash, _ourPeerId);
            await _stream.WriteAsync(hs, ct);
            await _stream.FlushAsync(ct);

            var hsBuf = new byte[68];
            var read = 0;
            while (read < 68)
            {
                var r = await _stream.ReadAsync(hsBuf.AsMemory(read, 68 - read), ct);
                if (r == 0) { System.Console.WriteLine($"[Peer {Ip}:{Port}] Handshake read 0"); return false; }
                read += r;
            }

            if (hsBuf[0] != 19) { System.Console.WriteLine($"[Peer {Ip}:{Port}] Bad pstrlen={hsBuf[0]}"); return false; }
            var proto = System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol");
            for (int i = 0; i < 19; i++)
                if (hsBuf[1 + i] != proto[i]) { System.Console.WriteLine($"[Peer {Ip}:{Port}] Bad protocol string"); return false; }
            for (int i = 0; i < 20; i++)
                if (hsBuf[28 + i] != _infoHash[i]) { System.Console.WriteLine($"[Peer {Ip}:{Port}] Bad info_hash"); return false; }

            System.Console.WriteLine($"[Peer {Ip}:{Port}] Handshake OK");
            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[Peer {Ip}:{Port}] Handshake error: {ex.Message}");
            return false;
        }
    }

    private static byte[] BuildMseHandshake(byte[] infoHash, string peerId)
    {
        var msg = new byte[68];
        msg[0] = 19;
        System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(msg, 1);
        infoHash.CopyTo(msg, 28);
        System.Text.Encoding.ASCII.GetBytes(peerId).CopyTo(msg, 48);
        return msg;
    }

    public async Task StartReadingAsync(CancellationToken ct)
    {
        if (_stream == null) return;
        var lenBuf = new byte[4];
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                var read = 0;
                while (read < 4)
                {
                    var r = await _stream.ReadAsync(lenBuf.AsMemory(read, 4 - read), ct);
                    if (r == 0) { Disconnect(); return; }
                    read += r;
                }
                var len = PeerWireMessages.ReadInt(lenBuf, 0);
                if (len == 0) continue;
                var msg = new byte[len];
                read = 0;
                while (read < len)
                {
                    var r = await _stream.ReadAsync(msg.AsMemory(read, len - read), ct);
                    if (r == 0) { Disconnect(); return; }
                    read += r;
                }
                HandleMessage(msg);
            }
        }
        catch
        {
            Disconnect();
        }
    }

    private void HandleMessage(byte[] msg)
    {
        if (msg.Length == 0) return;
        var id = (PeerMessageId)msg[0];
        System.Console.WriteLine($"[Peer {Ip}:{Port}] Got message: {id} (len={msg.Length})");

        if (id == PeerMessageId.Bitfield)
        {
            GotBitfield = true;
            var bits = msg.AsSpan(1);
            if (bits.Length == 0) return;
            _peerPieces = new System.Collections.BitArray(PieceCount);
            for (int i = 0; i < PieceCount && i / 8 < bits.Length; i++)
            {
                byte b = bits[i / 8];
                bool set = ((b >> (7 - i % 8)) & 1) == 1;
                _peerPieces[i] = set;
            }
            bool allSet = true;
            for (int i = 0; i < PieceCount; i++)
            {
                if (!_peerPieces[i]) { allSet = false; break; }
            }
            if (allSet) IsSeeder = true;
            System.Console.WriteLine($"[Peer {Ip}:{Port}] Bitfield len={bits.Length}, pieces={PieceCount}, isSeeder={allSet}");
        }
        else if (id == PeerMessageId.Have)
        {
            if (msg.Length >= 5)
            {
                int pieceIndex = (msg[1] << 24) | (msg[2] << 16) | (msg[3] << 8) | msg[4];
                if (_peerPieces == null) _peerPieces = new System.Collections.BitArray(PieceCount);
                if (pieceIndex >= 0 && pieceIndex < PieceCount)
                {
                    _peerPieces[pieceIndex] = true;
                    bool allSet = true;
                    for (int i = 0; i < PieceCount; i++)
                    {
                        if (!_peerPieces[i]) { allSet = false; break; }
                    }
                    if (allSet) { IsSeeder = true; System.Console.WriteLine($"[Peer {Ip}:{Port}] All pieces via Have, marking as seeder"); }
                }
            }
        }
        else if (id == PeerMessageId.Unchoke)
        {
            PeerChoking = false;
        }
        else if (id == PeerMessageId.Choke)
        {
            PeerChoking = true;
        }
        else if (id == PeerMessageId.Request)
        {
            Disconnect();
            return;
        }
        else if (id == PeerMessageId.Piece)
        {
            OnPiece?.Invoke(this, msg);
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (!IsConnected || _disposed || _stream == null) return;
        try
        {
            await _stream.WriteAsync(data, ct);
        }
        catch
        {
            Disconnect();
        }
    }

    public void Disconnect()
    {
        if (_disposed) return;
        IsConnected = false;
        try { _client.Close(); } catch { }
        OnDisconnected?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}

internal sealed class EncryptedStream : Stream
{
    private readonly Stream _inner;
    private readonly Rc4Cipher _encryptor;
    private readonly Rc4Cipher _decryptor;

    public EncryptedStream(Stream inner, Rc4Cipher encryptor, Rc4Cipher decryptor)
    {
        _inner = inner;
        _encryptor = encryptor;
        _decryptor = decryptor;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = _inner.Read(buffer, offset, count);
        if (read > 0) _decryptor.Decrypt(buffer, offset, read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int read = await _inner.ReadAsync(buffer, ct);
        if (read > 0)
        {
            var temp = buffer.Slice(0, read).ToArray();
            _decryptor.Decrypt(temp, 0, read);
            temp.CopyTo(buffer);
        }
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        _encryptor.Encrypt(copy, 0, count);
        _inner.Write(copy, 0, count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        var copy = buffer.ToArray();
        _encryptor.Encrypt(copy, 0, copy.Length);
        await _inner.WriteAsync(copy, ct);
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
