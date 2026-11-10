namespace SAS_Torrent.Avalonia.Localization;

public sealed class EnglishLanguage : IUiLanguage
{
    public AppLanguage Language => AppLanguage.English;

    public IReadOnlyDictionary<string, string> Strings { get; } = new Dictionary<string, string>
    {
        ["Common.Browse"] = "Browse...",
        ["Common.Reset"] = "Reset",

        ["MainWindow.NoUpdates"] = "No updates",
        ["MainWindow.CheckUpdatesTooltip"] = "Check for updates on GitHub",
        ["MainWindow.ReportBug"] = "Bug?",
        ["MainWindow.IssuesTooltip"] = "Open the GitHub issues page",
        ["MainWindow.LanguageTooltip"] = "Change language",
        ["MainWindow.QueueHeader"] = "Download queue",
        ["MainWindow.AddToQueue"] = "+ Add to queue",
        ["MainWindow.SaveFolderLabel"] = "Common save folder:",
        ["MainWindow.DropHere"] = "Drop here",
        ["MainWindow.RemoveFromQueueTooltip"] = "Remove from queue",
        ["MainWindow.StartAll"] = "Start all",
        ["MainWindow.StopAll"] = "Stop all",
        ["MainWindow.UpdateAvailable"] = "Update the client!",
        ["MainWindow.OpenLinkFailedFormat"] = "Failed to open the link: {0} ({1})",
        ["MainWindow.InvalidTorrentFile"] = "Invalid .torrent file format",
        ["MainWindow.TorrentAlreadyQueuedFormat"] = "This torrent is already in the queue: {0}",
        ["MainWindow.TorrentAddedFormat"] = "Torrent added: {0}",
        ["MainWindow.AddTorrentErrorFormat"] = "Error adding {0}: {1}",
        ["MainWindow.AllDownloadsStopped"] = "All downloads stopped",
        ["MainWindow.TorrentsStartedFormat"] = "Torrents started: {0}",
        ["MainWindow.NoTorrentsToStart"] = "No torrents to start",
        ["MainWindow.SelectTorrentFileTitle"] = "Select a torrent file",
        ["MainWindow.SelectDownloadFolderTitle"] = "Choose a folder for saving torrents",

        ["Torrent.PiecesCountFormat"] = "Pieces: {0}",
        ["Torrent.SelectedFilesFormat"] = "Selected files: {0} / {1}",
        ["Torrent.ButtonDownload"] = "Download",
        ["Torrent.ButtonStop"] = "Stop",
        ["Torrent.SpeedPrefix"] = "Speed:",
        ["Torrent.EtaPrefix"] = "ETA:",
        ["Torrent.StatusConnecting"] = "Connecting...",
        ["Torrent.StatusActivePeersFormat"] = "Active peers: {0} ({1}/{2})",
        ["Torrent.StatusCompleted"] = "Completed",
        ["Torrent.StatusStopped"] = "Stopped",
        ["Torrent.StatusErrorFormat"] = "Error: {0}",

        ["File.PiecesUnit"] = "pcs.",

        ["Settings.Title"] = "Settings",
        ["Settings.Header"] = "Download folder settings",
        ["Settings.SubfolderHint"] = "A separate subfolder is created for each torrent inside the selected directory.",
        ["Settings.OpenFolder"] = "Open folder",
        ["Settings.Save"] = "Save",
        ["Settings.Cancel"] = "Cancel",
        ["Settings.DefaultPathFormat"] = "Default: {0}",
        ["Settings.SelectFolderTitle"] = "Choose a download folder",
        ["Settings.OpenFolderFailedFormat"] = "Failed to open the folder: {0}",
        ["Settings.SaveFailedFormat"] = "Failed to save settings: {0}",

        ["Units.Byte"] = "B",
        ["Units.Kilobyte"] = "KB",
        ["Units.Megabyte"] = "MB",
        ["Units.Gigabyte"] = "GB",
        ["Units.PerSecond"] = "/s",
        ["Units.Minute"] = "min",
        ["Units.Hour"] = "h",
        ["Units.EtaCalculating"] = "calculating (15 s)..."
    };
}
