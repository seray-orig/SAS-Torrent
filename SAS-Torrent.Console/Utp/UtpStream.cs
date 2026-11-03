using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace SAS_Torrent.Console.Utp;

public sealed class UtpStream : Stream
{
    private const int HeaderSize = 20;
    private const int MaxPayload = 1400;
    private const int ReceiveWindow = 1024 * 1024;
    private const int DefaultWindow = 64 * 1024;
    private const int MaxWindow = 512 * 1024;
    private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetransmitTimeout = TimeSpan.FromMilliseconds(700);

    private readonly UdpClient _udp;
    private readonly IPEndPoint _remote;
    private readonly ushort _connectionIdRecv;
    private readonly ushort _connectionIdSend;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true
    });
    private readonly SemaphoreSlim _sendSignal = new(0);
    private readonly SemaphoreSlim _socketLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<ushort, SentPacket> _unacked = new();
    private readonly Dictionary<ushort, ReceivedPacket> _pending = new();
    private readonly List<byte> _sendBuffer = new();

    private ushort _nextSequence;
    private ushort _ackNumber;
    private ushort _lastAckSent;
    private uint _replyMicro;
    private int _peerWindow = DefaultWindow;
    private int _inFlightBytes;
    private int _congestionWindow = DefaultWindow;
    private int _bufferedBytes;
    private byte[]? _currentRead;
    private int _currentReadOffset;
    private bool _eofReceived;
    private bool _disposed;
    private DateTime _lastSend = DateTime.UtcNow;

    private UtpStream(UdpClient udp, IPEndPoint remote, ushort connectionIdRecv, ushort initialSequence)
    {
        _udp = udp;
        _remote = remote;
        _connectionIdRecv = connectionIdRecv;
        _connectionIdSend = (ushort)(connectionIdRecv + 1);
        _nextSequence = initialSequence;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public static async Task<UtpStream?> ConnectAsync(string ip, int port, TimeSpan timeout, CancellationToken ct)
    {
        if (!IPAddress.TryParse(ip, out var address)) return null;

        var udp = new UdpClient(address.AddressFamily);
        try
        {
            udp.Connect(new IPEndPoint(address, port));
            var connectionId = (ushort)Random.Shared.Next(1, 0xFFFE);
            var initialSequence = (ushort)Random.Shared.Next(1, 0xFFFE);
            var stream = new UtpStream(udp, new IPEndPoint(address, port), connectionId, initialSequence);
            if (!await stream.HandshakeAsync(timeout, ct))
            {
                stream.Dispose();
                return null;
            }

            stream.Start();
            return stream;
        }
        catch
        {
            udp.Dispose();
            return null;
        }
    }

    private async Task<bool> HandshakeAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshakeCts.CancelAfter(timeout);

        var syn = BuildPacket(PacketType.Syn, _connectionIdRecv, _nextSequence, 0, ReceiveWindow);
        _nextSequence++;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await SendRawAsync(syn, handshakeCts.Token);

            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(1000 * (attempt + 1));
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeCts.Token);
                    receiveCts.CancelAfter(remaining);
                    var result = await _udp.ReceiveAsync(receiveCts.Token);
                    var packet = ParsePacket(result.Buffer);
                    if (packet == null) continue;
                    if (packet.Value.Type != PacketType.State ||
                        packet.Value.ConnectionId != _connectionIdRecv)
                        continue;

                    lock (_stateLock)
                    {
                        _ackNumber = (ushort)(packet.Value.SequenceNumber - 1);
                        _lastAckSent = _ackNumber;
                        _peerWindow = packet.Value.WindowSize == 0
                            ? DefaultWindow
                            : (int)Math.Min(packet.Value.WindowSize, int.MaxValue);
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }

            if (handshakeCts.IsCancellationRequested) break;
        }

        return false;
    }

    private void Start()
    {
        _ = ReceiveLoopAsync(_cts.Token);
        _ = SendLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                await ProcessPacketAsync(result.Buffer, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) return;
                _incoming.Writer.TryComplete();
                return;
            }
            catch
            {
            }
        }
    }

    private async Task ProcessPacketAsync(byte[] buffer, CancellationToken ct)
    {
        var parsed = ParsePacket(buffer);
        if (parsed == null) return;

        var packet = parsed.Value;
        if (packet.ConnectionId != _connectionIdRecv) return;

        if (packet.Timestamp != 0)
            _replyMicro = CurrentMicroseconds() - packet.Timestamp;

        if (packet.Type == PacketType.Reset)
        {
            _incoming.Writer.TryComplete();
            return;
        }

        if (packet.Type == PacketType.Syn) return;

        ProcessAck(packet.AckNumber);

        var payloadLength = buffer.Length - packet.PayloadOffset;
        if (payloadLength < 0) return;

        if (packet.Type == PacketType.Data)
        {
            var payload = buffer.AsSpan(packet.PayloadOffset, payloadLength).ToArray();
            ProcessIncomingPayload(packet.SequenceNumber, payload, false);
            await SendAcknowledgementAsync(false, ct);
        }
        else if (packet.Type == PacketType.Fin)
        {
            ProcessIncomingPayload(packet.SequenceNumber, Array.Empty<byte>(), true);
            await SendAcknowledgementAsync(true, ct);
        }
    }

    private void ProcessAck(ushort ackNumber)
    {
        var wakeSender = false;
        lock (_stateLock)
        {
            if (SeqAfter(ackNumber, (ushort)(_nextSequence - 1))) return;

            foreach (var sequence in _unacked.Keys.ToList())
            {
                if (SeqAfter(sequence, ackNumber)) continue;
                var sent = _unacked[sequence];
                _unacked.Remove(sequence);
                _inFlightBytes -= sent.PayloadLength;
                _congestionWindow = Math.Min(_congestionWindow + sent.PayloadLength, MaxWindow);
                wakeSender = true;
            }
        }

        if (wakeSender)
            _sendSignal.Release();
    }

    private void ProcessIncomingPayload(ushort sequence, byte[] payload, bool isFin)
    {
        lock (_stateLock)
        {
            if (_eofReceived) return;

            var expected = (ushort)(_ackNumber + 1);
            if (sequence == expected)
            {
                DeliverLocked(sequence, payload, isFin);
                DrainPendingLocked();
            }
            else if (SeqAfter(sequence, expected) && _pending.Count < 1024)
            {
                _pending[sequence] = new ReceivedPacket(payload, isFin);
            }
            else if (isFin && sequence == _ackNumber)
            {
                _eofReceived = true;
                _incoming.Writer.TryComplete();
            }
        }
    }

    private void DrainPendingLocked()
    {
        while (!_eofReceived && _pending.TryGetValue((ushort)(_ackNumber + 1), out var pending))
        {
            _pending.Remove((ushort)(_ackNumber + 1));
            DeliverLocked((ushort)(_ackNumber + 1), pending.Payload, pending.IsFin);
        }
    }

    private void DeliverLocked(ushort sequence, byte[] payload, bool isFin)
    {
        _ackNumber = sequence;
        if (payload.Length > 0)
        {
            _bufferedBytes += payload.Length;
            _incoming.Writer.TryWrite(payload);
        }

        if (isFin)
        {
            _eofReceived = true;
            _incoming.Writer.TryComplete();
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _sendSignal.WaitAsync(SendInterval, ct);
                await SendQueuedDataAsync(ct);
                await RetransmitAsync(ct);
                await KeepAliveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    private async Task SendQueuedDataAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[] payload;
            ushort sequence;
            byte[] packet;
            lock (_stateLock)
            {
                if (_sendBuffer.Count == 0) return;

                var length = Math.Min(MaxPayload, _sendBuffer.Count);
                if (!CanSendLocked(length)) return;

                payload = _sendBuffer.GetRange(0, length).ToArray();
                _sendBuffer.RemoveRange(0, length);

                sequence = _nextSequence++;
                packet = BuildPacket(PacketType.Data, _connectionIdSend, sequence, _ackNumber, CalculateWindowLocked(), payload);
                _unacked[sequence] = new SentPacket(payload);
                _inFlightBytes += payload.Length;
            }

            await SendRawAsync(packet, ct);
        }
    }

    private async Task RetransmitAsync(CancellationToken ct)
    {
        List<byte[]>? resend = null;
        lock (_stateLock)
        {
            var now = DateTime.UtcNow;
            foreach (var (sequence, sent) in _unacked)
            {
                if (now - sent.SentAt < RetransmitTimeout) continue;
                sent.SentAt = now;
                sent.Transmissions++;
                _congestionWindow = Math.Max(_congestionWindow / 2, MaxPayload * 4);
                resend ??= new List<byte[]>();
                resend.Add(BuildPacket(PacketType.Data, _connectionIdSend, sequence, _ackNumber, CalculateWindowLocked(), sent.Payload));
            }
        }

        if (resend == null) return;
        foreach (var packet in resend)
            await SendRawAsync(packet, ct);
    }

    private async Task KeepAliveAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastSend < KeepAliveInterval) return;
        await SendAcknowledgementAsync(true, ct);
    }

    private async Task SendAcknowledgementAsync(bool force, CancellationToken ct)
    {
        byte[] packet;
        lock (_stateLock)
        {
            if (!force && _ackNumber == _lastAckSent) return;
            _lastAckSent = _ackNumber;
            packet = BuildPacket(PacketType.State, _connectionIdSend, _nextSequence, _ackNumber, CalculateWindowLocked());
        }

        await SendRawAsync(packet, ct);
    }

    private int CalculateWindowLocked()
    {
        return Math.Max(ReceiveWindow - _bufferedBytes, MaxPayload);
    }

    private bool CanSendLocked(int payloadLength)
    {
        var window = Math.Min(_congestionWindow, _peerWindow);
        return _inFlightBytes + payloadLength <= window;
    }

    private async Task SendRawAsync(byte[] packet, CancellationToken ct)
    {
        try
        {
            await _socketLock.WaitAsync(ct);
            try
            {
                if (_disposed) return;
                await _udp.SendAsync(packet, ct);
                _lastSend = DateTime.UtcNow;
            }
            finally
            {
                _socketLock.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private byte[] BuildPacket(
        PacketType type,
        ushort connectionId,
        ushort sequence,
        ushort ackNumber,
        int window,
        byte[]? payload = null)
    {
        var payloadLength = payload?.Length ?? 0;
        var packet = new byte[HeaderSize + payloadLength];

        packet[0] = (byte)(((int)type << 4) | 1);
        packet[1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), CurrentMicroseconds());
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), _replyMicro);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), (uint)Math.Max(window, 0));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18), ackNumber);

        payload?.CopyTo(packet, HeaderSize);
        return packet;
    }

    private static ParsedPacket? ParsePacket(byte[] buffer)
    {
        if (buffer.Length < HeaderSize) return null;

        var version = buffer[0] & 0x0F;
        var type = buffer[0] >> 4;
        if (version != 1 || type > (int)PacketType.Syn) return null;

        var extension = buffer[1];
        var connectionId = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
        var timestamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4));
        var window = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12));
        var sequence = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(16));
        var ackNumber = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(18));

        var offset = HeaderSize;
        while (extension != 0)
        {
            if (offset + 2 > buffer.Length) return null;
            var nextExtension = buffer[offset];
            var extensionLength = buffer[offset + 1];
            offset += 2 + extensionLength;
            if (offset > buffer.Length) return null;
            extension = nextExtension;
        }

        return new ParsedPacket(
            (PacketType)type,
            connectionId,
            sequence,
            ackNumber,
            timestamp,
            window,
            offset);
    }

    private static uint CurrentMicroseconds()
    {
        return (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000);
    }

    private static bool SeqAfter(ushort a, ushort b)
    {
        return (short)(a - b) > 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;

        if (_currentRead == null)
        {
            try
            {
                _currentRead = await _incoming.Reader.ReadAsync(cancellationToken);
                _currentReadOffset = 0;
                lock (_stateLock)
                    _bufferedBytes = Math.Max(0, _bufferedBytes - _currentRead.Length);
            }
            catch (ChannelClosedException)
            {
                return 0;
            }
        }

        var count = Math.Min(buffer.Length, _currentRead.Length - _currentReadOffset);
        _currentRead.AsSpan(_currentReadOffset, count).CopyTo(buffer.Span);
        _currentReadOffset += count;
        if (_currentReadOffset >= _currentRead.Length)
            _currentRead = null;

        return count;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UtpStream));
        if (buffer.Length == 0) return ValueTask.CompletedTask;

        lock (_stateLock)
        {
            if (_sendBuffer.Count > 32 * 1024 * 1024)
                throw new IOException("Переполнен буфер отправки uTP");
            _sendBuffer.AddRange(buffer.ToArray());
        }

        _sendSignal.Release();
        return ValueTask.CompletedTask;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    }

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            try { _cts.Cancel(); } catch { }
            try { _udp.Dispose(); } catch { }
            _incoming.Writer.TryComplete();
            try { _cts.Dispose(); } catch { }
        }

        base.Dispose(disposing);
    }

    private enum PacketType
    {
        Data = 0,
        Fin = 1,
        State = 2,
        Reset = 3,
        Syn = 4
    }

    private readonly struct ParsedPacket
    {
        public ParsedPacket(
            PacketType type,
            ushort connectionId,
            ushort sequenceNumber,
            ushort ackNumber,
            uint timestamp,
            uint windowSize,
            int payloadOffset)
        {
            Type = type;
            ConnectionId = connectionId;
            SequenceNumber = sequenceNumber;
            AckNumber = ackNumber;
            Timestamp = timestamp;
            WindowSize = windowSize;
            PayloadOffset = payloadOffset;
        }

        public PacketType Type { get; }
        public ushort ConnectionId { get; }
        public ushort SequenceNumber { get; }
        public ushort AckNumber { get; }
        public uint Timestamp { get; }
        public uint WindowSize { get; }
        public int PayloadOffset { get; }
    }

    private sealed class SentPacket
    {
        public SentPacket(byte[] payload)
        {
            Payload = payload;
        }

        public byte[] Payload { get; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public int Transmissions { get; set; } = 1;

        public int PayloadLength => Payload.Length;
    }

    private sealed class ReceivedPacket
    {
        public ReceivedPacket(byte[] payload, bool isFin)
        {
            Payload = payload;
            IsFin = isFin;
        }

        public byte[] Payload { get; }
        public bool IsFin { get; }
    }
}
