namespace SAS_Torrent.Console.Bencode;

public sealed class BencodeException : Exception
{
    public BencodeException(string message) : base(message) { }
    public BencodeException(string message, Exception inner) : base(message, inner) { }
}
