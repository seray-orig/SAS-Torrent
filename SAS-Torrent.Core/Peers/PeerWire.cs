namespace SAS_Torrent.Core.Peers;

public enum PeerMessageId : byte
{
    Choke = 0,
    Unchoke = 1,
    Interested = 2,
    NotInterested = 3,
    Have = 4,
    Bitfield = 5,
    Request = 6,
    Piece = 7,
    Cancel = 8
}

public static class PeerWireMessages
{
    public static byte[] BuildHandshake(byte[] infoHash, string peerId)
    {
        var msg = new byte[68];
        msg[0] = 19;
        System.Text.Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(msg, 1);
        infoHash.CopyTo(msg, 28);
        System.Text.Encoding.ASCII.GetBytes(peerId).CopyTo(msg, 48);
        return msg;
    }

    public static byte[] BuildInterested() => BuildSimple((byte)PeerMessageId.Interested);
    public static byte[] BuildNotInterested() => BuildSimple((byte)PeerMessageId.NotInterested);
    public static byte[] BuildChoke() => BuildSimple((byte)PeerMessageId.Choke);

    private static byte[] BuildSimple(byte id)
    {
        var msg = new byte[5];
        WriteInt(msg, 0, 1);
        msg[4] = id;
        return msg;
    }

    public static byte[] BuildRequest(int index, int begin, int length)
    {
        var msg = new byte[17];
        WriteInt(msg, 0, 13);
        msg[4] = (byte)PeerMessageId.Request;
        WriteInt(msg, 5, index);
        WriteInt(msg, 9, begin);
        WriteInt(msg, 13, length);
        return msg;
    }

    private static void WriteInt(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    public static int ReadInt(byte[] buf, int offset)
    {
        return (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
    }
}
