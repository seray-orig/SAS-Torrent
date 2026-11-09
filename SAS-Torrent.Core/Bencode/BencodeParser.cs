using System.Text;

namespace SAS_Torrent.Core.Bencode;

public sealed class BencodeParser
{
    private readonly byte[] _data;
    private int _pos;

    public BencodeParser(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _pos = 0;
    }

    public BencodeValue Parse()
    {
        if (_pos >= _data.Length)
            throw new BencodeException("Unexpected end of data");

        var c = (char)_data[_pos];

        if (char.IsDigit(c))
            return ParseString();

        if (c == 'i')
            return ParseInt();

        if (c == 'l')
            return ParseList();

        if (c == 'd')
            return ParseDictionary();

        throw new BencodeException($"Unexpected character '{c}' at position {_pos}");
    }

    private BencodeString ParseString()
    {
        var lenStart = _pos;
        while (_pos < _data.Length && _data[_pos] != ':')
            _pos++;

        if (_pos >= _data.Length || _data[_pos] != ':')
            throw new BencodeException("Invalid string length");

        var lenStr = Encoding.ASCII.GetString(_data, lenStart, _pos - lenStart);
        if (!int.TryParse(lenStr, out var length))
            throw new BencodeException("Invalid string length number");

        _pos++;
        if (_pos + length > _data.Length)
            throw new BencodeException("String data out of range");

        var bytes = new byte[length];
        Buffer.BlockCopy(_data, _pos, bytes, 0, length);
        _pos += length;

        return new BencodeString(bytes);
    }

    private BencodeInt ParseInt()
    {
        if (_data[_pos] != 'i')
            throw new BencodeException("Invalid integer");

        _pos++;
        var start = _pos;
        while (_pos < _data.Length && _data[_pos] != 'e')
            _pos++;

        if (_pos >= _data.Length || _data[_pos] != 'e')
            throw new BencodeException("Invalid integer end");

        var numStr = Encoding.ASCII.GetString(_data, start, _pos - start);
        _pos++;

        if (!long.TryParse(numStr, out var value))
            throw new BencodeException("Invalid integer value");

        return new BencodeInt(value);
    }

    private BencodeList ParseList()
    {
        if (_data[_pos] != 'l')
            throw new BencodeException("Invalid list");

        _pos++;
        var list = new BencodeList();

        while (_pos < _data.Length && _data[_pos] != 'e')
        {
            list.Items.Add(Parse());
        }

        if (_pos >= _data.Length || _data[_pos] != 'e')
            throw new BencodeException("Invalid list end");

        _pos++;
        return list;
    }

    private BencodeDictionary ParseDictionary()
    {
        if (_data[_pos] != 'd')
            throw new BencodeException("Invalid dictionary");

        _pos++;
        var dict = new BencodeDictionary();

        while (_pos < _data.Length && _data[_pos] != 'e')
        {
            var keyValue = Parse();
            if (keyValue is not BencodeString keyStr)
                throw new BencodeException("Dictionary key must be string");

            var value = Parse();
            dict.Entries[keyStr.Text] = value;
        }

        if (_pos >= _data.Length || _data[_pos] != 'e')
            throw new BencodeException("Invalid dictionary end");

        _pos++;
        return dict;
    }

    public static BencodeValue Parse(byte[] data) => new BencodeParser(data).Parse();
}
