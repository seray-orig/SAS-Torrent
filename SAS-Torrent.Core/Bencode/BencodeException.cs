namespace SAS_Torrent.Core.Bencode;

public sealed class BencodeException : Exception
{
    public BencodeException(string message) : base(message) { }
    public BencodeException(string message, Exception inner) : base(message, inner) { }
}
