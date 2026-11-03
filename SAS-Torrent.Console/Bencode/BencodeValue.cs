using System.Text;

namespace SAS_Torrent.Console.Bencode;

public abstract class BencodeValue
{
    public abstract BencodeType Type { get; }
}

public sealed class BencodeString : BencodeValue
{
    public byte[] Bytes { get; }
    public string Text => Encoding.UTF8.GetString(Bytes);
    public override BencodeType Type => BencodeType.String;

    public BencodeString(byte[] bytes)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }
}

public sealed class BencodeInt : BencodeValue
{
    public long Value { get; }
    public override BencodeType Type => BencodeType.Integer;

    public BencodeInt(long value)
    {
        Value = value;
    }
}

public sealed class BencodeList : BencodeValue
{
    public List<BencodeValue> Items { get; } = new();
    public override BencodeType Type => BencodeType.List;
}

public sealed class BencodeDictionary : BencodeValue
{
    public Dictionary<string, BencodeValue> Entries { get; } = new(StringComparer.Ordinal);
    public override BencodeType Type => BencodeType.Dictionary;
}
