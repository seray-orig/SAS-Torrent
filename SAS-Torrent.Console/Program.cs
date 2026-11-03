using System.Security.Cryptography;
using SAS_Torrent.Console.Bencode;
using SAS_Torrent.Console.IO;
using SAS_Torrent.Console.Peers;
using SAS_Torrent.Console.Torrent;

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
    Console.WriteLine($"Торрент: {torrent.Name}");
    Console.WriteLine($"Размер: {torrent.TotalLength} байт");
    Console.WriteLine($"Кусков: {Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength)}");

    var peerId = "-qB4640-" + RandomNumberGenerator.GetInt32(100000000).ToString("D8") + RandomNumberGenerator.GetInt32(10000).ToString("D4");
    var port = 6881;
    var pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);

    var peerManager = new PeerManager(torrent.InfoHash, peerId, pieceCount);
    var discovery = new PeerDiscovery(torrent, peerId, port);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var state = DownloadState.Load(torrent);
    if (state.Pieces.Count > 0)
        Console.WriteLine($"[Resume] Найден прогресс: {state.Pieces.Count} кусков ({state.FilePath})");

    Console.WriteLine("Параллельный поиск пиров (трекеры и DHT) и подключение...");
    var connectTask = peerManager.ConnectToPeersAsync(discovery.DiscoverAsync(cts.Token), cts.Token);

    Directory.CreateDirectory(downloadDir);
    var fileWriter = new FileWriter(torrent, downloadDir);
    var downloadManager = new DownloadManager(torrent, peerManager, fileWriter, state);

    var completed = await downloadManager.DownloadAsync(cts.Token);

    cts.Cancel();
    try { await connectTask; } catch (OperationCanceledException) { }

    state.Flush();
    fileWriter.Close();
    peerManager.DisconnectAll();

    if (completed)
        Console.WriteLine("Готово.");
    else
        Console.WriteLine($"Загрузка не завершена. Кусков: {downloadManager.CompletedPieces}/{pieceCount}. Прогресс сохранён: {state.FilePath}");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
