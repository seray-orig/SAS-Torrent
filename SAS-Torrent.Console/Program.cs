using System.Security.Cryptography;
using SAS_Torrent.Console.Bencode;
using SAS_Torrent.Console.IO;
using SAS_Torrent.Console.Peers;
using SAS_Torrent.Console.Torrent;
using SAS_Torrent.Console.Tracker;

if (args.Length != 0)
{
    Console.WriteLine("Использование: SAS-Torrent.Console <файл.torrent> [папка_скачивания] " + $"{args[0]}");
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
    TrackerResponse? trackerResp = null;
    Console.WriteLine("Обращаемся к трекеру...");
    var trackers = new List<string>();
    if (!string.IsNullOrEmpty(torrent.Announce))
        trackers.Add(torrent.Announce);
    foreach (var t in torrent.AnnounceList)
        if (!trackers.Contains(t))
            trackers.Add(t);

    foreach (var tr in trackers)
    {
        try
        {
            var announceUri = new Uri(tr);
            if (announceUri.Scheme == "udp")
            {
                var udpTracker = new UdpTrackerClient(torrent, peerId, port, announceUri);
                trackerResp = await udpTracker.AnnounceAsync(CancellationToken.None);
                break;
            }
            else if (announceUri.Scheme == "http" || announceUri.Scheme == "https")
            {
                var tracker = new TrackerClient(torrent, peerId, port, tr);
                trackerResp = await tracker.AnnounceAsync(CancellationToken.None);
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Трекер {tr} не сработал: {ex.Message}");
        }
    }

    if (trackerResp == null || trackerResp.Peers.Count == 0)
    {
        Console.WriteLine("Пробуем найти пиры через DHT...");
        var dht = new SAS_Torrent.Console.Dht.DhtClient(torrent.InfoHash, peerId, port);
        var dhtPeers = await dht.FindPeersAsync(CancellationToken.None);
        trackerResp = new TrackerResponse(dhtPeers, 1800);
    }
    Console.WriteLine($"Найдено пиров: {trackerResp.Peers.Count}");

    var pieceCount = (int)Math.Ceiling((double)torrent.TotalLength / torrent.PieceLength);
    var peerManager = new PeerManager(torrent.InfoHash, peerId, pieceCount);
    using var cts = new CancellationTokenSource();
    await peerManager.ConnectToPeersAsync(trackerResp.Peers, cts.Token);
    
    Console.WriteLine("Ожидание битфилдов и фильтрация только сидов...");
    await Task.Delay(3000);
    var seeders = peerManager.GetSeeders();
    Console.WriteLine($"Сидов с полным файлом: {seeders.Count}");
    
    if (seeders.Count == 0)
    {
        Console.WriteLine("Нет сидов с 100% файлом. Выход.");
        peerManager.DisconnectAll();
        return;
    }

    Directory.CreateDirectory(downloadDir);
    var fileWriter = new FileWriter(torrent, downloadDir);
    var downloadManager = new DownloadManager(torrent, peerManager, fileWriter);
    await downloadManager.DownloadAsync(cts.Token);
    fileWriter.Close();
    peerManager.DisconnectAll();
    Console.WriteLine("Готово.");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
