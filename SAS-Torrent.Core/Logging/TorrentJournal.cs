namespace SAS_Torrent.Core.Logging;

public sealed class TorrentJournal
{
    public string TorrentName { get; }
    public long TotalLength { get; }
    public int PieceCount { get; }
    public int PiecesCompleted { get; private set; }
    public int ConnectedSeeders { get; private set; }
    public int TotalConnected { get; private set; }

    public TorrentJournal(string torrentName, long totalLength, int pieceCount)
    {
        TorrentName = torrentName;
        TotalLength = totalLength;
        PieceCount = pieceCount;
    }

    public void UpdateProgress(int completedPieces)
    {
        PiecesCompleted = completedPieces;
    }

    public void UpdateConnections(int totalConnected, int seeders)
    {
        TotalConnected = totalConnected;
        ConnectedSeeders = seeders;
    }

    public string GetSummary()
    {
        return $"Торрент: {TorrentName}\n" +
               $"Размер: {TotalLength} байт\n" +
               $"Кусков: {PieceCount}\n" +
               $"Есть кусков: {PiecesCompleted}/{PieceCount}\n" +
               $"Сидов подключено: {ConnectedSeeders}\n" +
               $"Всего подключено: {TotalConnected}";
    }
}
