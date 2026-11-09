namespace SAS_Torrent.Core.Peers.Transport;

public sealed class PeerTransportConnection : IDisposable
{
    private readonly IDisposable? _owner;

    public PeerTransportConnection(Stream stream, string name, IDisposable? owner)
    {
        Stream = stream;
        Name = name;
        _owner = owner;
    }

    public Stream Stream { get; }
    public string Name { get; }

    public void Dispose()
    {
        try { Stream.Dispose(); } catch { }

        if (_owner != null && !ReferenceEquals(_owner, Stream))
        {
            try { _owner.Dispose(); } catch { }
        }
    }
}
