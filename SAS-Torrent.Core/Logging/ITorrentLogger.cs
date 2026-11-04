namespace SAS_Torrent.Core.Logging;

public interface ITorrentLogger
{
    void Log(string message);
    void LogError(string message);
}
