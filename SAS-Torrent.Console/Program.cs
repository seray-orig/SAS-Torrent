using System.Security.Cryptography;
using SAS_Torrent.Core.Bencode;
using SAS_Torrent.Core.Config;
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

var config = AppConfig.EnsureCreated();

var torrentPath = args[0];

string downloadDir;

try
{
    var torrentBytes = File.ReadAllBytes(torrentPath);
    var root = BencodeParser.Parse(torrentBytes) as BencodeDictionary;
    if (root == null) throw new Exception("Неверный торрент-файл");

    var torrent = new TorrentInfo(root);

    if (args.Length > 1)
        downloadDir = Path.Combine(args[1], FileWriter.SanitizeName(torrent.Name));
    else
        downloadDir = config.ResolveDownloadDirectory(FileWriter.SanitizeName(torrent.Name));

    var pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);

    // --- Выбор файлов для скачивания ---
    Console.WriteLine($"Торрент: {torrent.Name}");
    Console.WriteLine($"Размер: {torrent.TotalLength} байт");
    Console.WriteLine($"Файлов: {torrent.Files.Count}");
    Console.WriteLine();

    for (int i = 0; i < torrent.Files.Count; i++)
    {
        var f = torrent.Files[i];
        Console.WriteLine($"  [{i}] {f.RelativePath} ({f.Length} байт)");
    }

    if (torrent.Files.Count > 1)
    {
        Console.WriteLine();
        Console.Write("Введите номера файлов для ПРОПУСКА (через запятую), или Enter чтобы скачать всё: ");
        var skipInput = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(skipInput))
        {
            foreach (var part in skipInput.Split(','))
            {
                var trimmed = part.Trim();
                if (int.TryParse(trimmed, out var idx) && idx >= 0 && idx < torrent.Files.Count)
                    torrent.Files[idx].IsSelected = false;
            }
        }
    }

    var selectedFiles = torrent.Files.Count(f => f.IsSelected);
    Console.WriteLine($"Будут скачаны файлов: {selectedFiles} из {torrent.Files.Count}");
    Console.WriteLine();

    var fileWriter = new FileWriter(torrent, downloadDir);
    var selectedPieceCount = fileWriter.SelectedPieceCount;

    var journal = new TorrentJournal(torrent.Name, torrent.TotalLength, selectedPieceCount);
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

    var state = DownloadState.Load(torrent, downloadDir);
    if (state.Pieces.Count > 0)
        logger.Log($"[Resume] Найден прогресс: {state.Pieces.Count} кусков ({state.FilePath})");

    logger.Log("Параллельный поиск пиров (трекеры и DHT) и подключение...");
    var connectTask = peerManager.ConnectToPeersAsync(discovery.DiscoverAsync(cts.Token), cts.Token);

    Directory.CreateDirectory(downloadDir);
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
        logger.Log($"Загрузка не завершена. Кусков: {downloadManager.CompletedPieces}/{selectedPieceCount}. Прогресс сохранён: {state.FilePath}");
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
