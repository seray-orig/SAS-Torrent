using Avalonia.Threading;
using SAS_Torrent.Core.Logging;

namespace SAS_Torrent.Avalonia;

public sealed class UiTorrentLogger : ITorrentLogger
{
    private readonly Action<string> _append;

    public UiTorrentLogger(Action<string> append)
    {
        _append = append;
    }

    public void Log(string message) => Append(message);

    public void LogError(string message) => Append($"[Ошибка] {message}");

    private void Append(string message)
    {
        var text = $"{DateTime.Now:HH:mm:ss} {message}";

        if (Dispatcher.UIThread.CheckAccess())
            _append(text);
        else
            Dispatcher.UIThread.Post(() => _append(text));
    }
}
