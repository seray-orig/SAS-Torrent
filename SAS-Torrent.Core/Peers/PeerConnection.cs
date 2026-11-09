using System.Security.Cryptography;
using SAS_Torrent.Core.Logging;
using SAS_Torrent.Core.Peers.Transport;
using SAS_Torrent.Core.Torrent;

namespace SAS_Torrent.Core.Peers;

public sealed class PeerConnection : IDisposable
{
    private readonly IReadOnlyList<IPeerTransport> _transports;
    private PeerTransportConnection? _transport;
    private Stream? _stream;
    private readonly byte[] _infoHash;
    private readonly string _ourPeerId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;
    private readonly ITorrentLogger? _logger;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    public string Ip { get; }
    public int Port { get; }
    public string TransportName { get; private set; } = "";
    public bool IsConnected { get; private set; }
    public bool IsSeeder { get; private set; }
    public bool GotBitfield { get; private set; }
    public bool Choked { get; private set; } = true;
    public bool PeerChoking { get; private set; } = true;
    public int PieceCount { get; set; } = 0;
    private System.Collections.BitArray? _peerPieces;

    public event Action<PeerConnection, byte[]>? OnPiece;
    public event Action<PeerConnection>? OnDisconnected;

    public PeerConnection(string ip, int port, byte[] infoHash, string ourPeerId, IReadOnlyList<IPeerTransport> transports, ITorrentLogger? logger = null)
    {
        Ip = ip;
        Port = port;
        _infoHash = infoHash;
        _ourPeerId = ourPeerId;
        _transports = transports;
        _stream = null;
        _logger = logger;
    }

    public async Task<bool> ConnectAndHandshakeAsync(CancellationToken ct)
    {
        try
        {
            _logger?.Log($"[Peer] Connecting...");
            _transport = await PeerTransportConnector.ConnectAnyAsync(_transports, Ip, Port, ConnectTimeout, FallbackDelay, ct);
            if (_transport == null)
            {
                _logger?.Log($"[Peer] Ни TCP, ни uTP не ответили");
                return false;
            }

            TransportName = _transport.Name;
            _stream = _transport.Stream;
            _logger?.Log($"[Peer] Подключение через {TransportName}, отправляем handshake");

            var hs = PeerWireMessages.BuildHandshake(_infoHash, _ourPeerId);
            await _stream.WriteAsync(hs, ct);
            await _stream.FlushAsync(ct);

            var hsBuf = new byte[68];
            var read = 0;
            while (read < 68)
            {
                var r = await _stream.ReadAsync(hsBuf.AsMemory(read, 68 - read), ct).AsTask().WaitAsync(HandshakeTimeout, ct);
                if (r == 0) { _logger?.Log($"[Peer] Handshake read 0"); return false; }
                read += r;
            }

            if (hsBuf[0] != 19) { _logger?.Log($"[Peer] Bad pstrlen={hsBuf[0]}"); return false; }
            var proto = System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol");
            for (int i = 0; i < 19; i++)
                if (hsBuf[1 + i] != proto[i]) { _logger?.Log($"[Peer] Bad protocol string"); return false; }
            for (int i = 0; i < 20; i++)
                if (hsBuf[28 + i] != _infoHash[i]) { _logger?.Log($"[Peer] Bad info_hash"); return false; }

            _logger?.Log($"[Peer] Handshake OK");
            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[Peer] Handshake error: {ex.Message}");
            return false;
        }
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
            _logger?.Log($"[Peer] Bitfield len={bits.Length}, pieces={PieceCount}, isSeeder={allSet}");
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
                    if (allSet) { IsSeeder = true; _logger?.Log($"[Peer] All pieces via Have, marking as seeder"); }
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

    public Task SendInterestedAsync(CancellationToken ct)
    {
        return SendMessageAsync(PeerWireMessages.BuildInterested(), ct);
    }

    public Task SendRequestAsync(int index, int begin, int length, CancellationToken ct)
    {
        return SendMessageAsync(PeerWireMessages.BuildRequest(index, begin, length), ct);
    }

    private Task SendMessageAsync(byte[] data, CancellationToken ct)
    {
        if (!PeerOutboundPolicy.IsAllowed(data))
            throw new InvalidOperationException("Outbound peer message is not allowed by the P2O policy.");

        return SendAllowedMessageAsync(data, ct);
    }

    private async Task SendAllowedMessageAsync(byte[] data, CancellationToken ct)
    {
        if (!IsConnected || _disposed || _stream == null) return;
        try
        {
            await _writeLock.WaitAsync(ct);
            try
            {
                await _stream.WriteAsync(data, ct);
            }
            finally
            {
                _writeLock.Release();
            }
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
        try { _transport?.Dispose(); } catch { }
        OnDisconnected?.Invoke(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
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
}
