using System.Security.Cryptography;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.IO;
using SAS_Torrent.Core.Logging;
using SAS_Torrent.Core.Peers;
using SAS_Torrent.Core.Torrent;

if (args.Length == 0)
{
    Console.WriteLine("Использование: SAS-Torrent.Console <файл.torrent> [папка_скачивания]");
    Console.ReadLine();
    return;
}

var torrentPath = args[0];
var downloadDir = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "downloads");

try
{
    var torrentBytes = File.ReadAllBytes(torrentPath);
    var root = BencodeParser.Parse(torrentBytes) as BencodeDictionary;
    if (root == null) throw new Exception("Неверный торрент-файл");

    var torrent = new TorrentInfo(root);
    var pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);

    var journal = new TorrentJournal(torrent.Name, torrent.TotalLength, pieceCount);
    var logger = new ConsoleLogger();
    logger.Log(journal.GetSummary());

    var peerId = "-qB4640-" + RandomNumberGenerator.GetInt32(100000000).ToString("D8") + RandomNumberGenerator.GetInt32(10000).ToString("D4");
    var port = 6881;

    var peerManager = new PeerManager(torrent.InfoHash, peerId, pieceCount, logger, journal);
    var discovery = new PeerDiscovery(torrent, peerId, port, logger);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var state = DownloadState.Load(torrent);
    if (state.Pieces.Count > 0)
        logger.Log($"[Resume] Найден прогресс: {state.Pieces.Count} кусков ({state.FilePath})");

    logger.Log("Параллельный поиск пиров (трекеры и DHT) и подключение...");
    var connectTask = peerManager.ConnectToPeersAsync(discovery.DiscoverAsync(cts.Token), cts.Token);

    Directory.CreateDirectory(downloadDir);
    var fileWriter = new FileWriter(torrent, downloadDir);
    var downloadManager = new DownloadManager(torrent, peerManager, fileWriter, state, logger, journal);

    var completed = await downloadManager.DownloadAsync(cts.Token);

    cts.Cancel();
    try { await connectTask; } catch (OperationCanceledException) { }

    state.Flush();
    fileWriter.Close();
    peerManager.DisconnectAll();

    logger.Log(journal.GetSummary());

    if (completed)
        logger.Log("Готово.");
    else
        logger.Log($"Загрузка не завершена. Кусков: {downloadManager.CompletedPieces}/{pieceCount}. Прогресс сохранён: {state.FilePath}");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

public sealed class ConsoleLogger : ITorrentLogger
{
    public void Log(string message)
    {
        Console.WriteLine(message);
    }

    public void LogError(string message)
    {
        Console.WriteLine($"Ошибка: {message}");
    }
}
