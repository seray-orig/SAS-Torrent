namespace SAS_Torrent.Avalonia.Localization;

public sealed class RussianLanguage : IUiLanguage
{
    public AppLanguage Language => AppLanguage.Russian;

    public IReadOnlyDictionary<string, string> Strings { get; } = new Dictionary<string, string>
    {
        ["Common.Browse"] = "Обзор...",
        ["Common.Reset"] = "Сбросить",

        ["MainWindow.NoUpdates"] = "Нет обновлений",
        ["MainWindow.CheckUpdatesTooltip"] = "Проверить обновления на GitHub",
        ["MainWindow.ReportBug"] = "Ошибка?",
        ["MainWindow.IssuesTooltip"] = "Открыть страницу issues на GitHub",
        ["MainWindow.LanguageTooltip"] = "Сменить язык",
        ["MainWindow.QueueHeader"] = "Очередь загрузок",
        ["MainWindow.AddToQueue"] = "+ Добавить в очередь",
        ["MainWindow.SaveFolderLabel"] = "Общая папка сохранения:",
        ["MainWindow.DropHere"] = "Перетащи сюда",
        ["MainWindow.RemoveFromQueueTooltip"] = "Удалить из очереди",
        ["MainWindow.StartAll"] = "Начать все",
        ["MainWindow.StopAll"] = "Остановить все",
        ["MainWindow.UpdateAvailable"] = "Обнови клиент!",
        ["MainWindow.OpenLinkFailedFormat"] = "Не удалось открыть ссылку: {0} ({1})",
        ["MainWindow.InvalidTorrentFile"] = "Неверный формат .torrent файла",
        ["MainWindow.TorrentAlreadyQueuedFormat"] = "Торрент уже есть в очереди: {0}",
        ["MainWindow.TorrentAddedFormat"] = "Добавлен торрент: {0}",
        ["MainWindow.AddTorrentErrorFormat"] = "Ошибка добавления {0}: {1}",
        ["MainWindow.AllDownloadsStopped"] = "Все загрузки остановлены",
        ["MainWindow.TorrentsStartedFormat"] = "Запущено торрентов: {0}",
        ["MainWindow.NoTorrentsToStart"] = "Нет торрентов для запуска",
        ["MainWindow.SelectTorrentFileTitle"] = "Выберите торрент-файл",
        ["MainWindow.SelectDownloadFolderTitle"] = "Выберите папку для сохранения торрентов",

        ["Torrent.PiecesCountFormat"] = "Кусков: {0}",
        ["Torrent.SelectedFilesFormat"] = "Выбрано файлов: {0} / {1}",
        ["Torrent.ButtonDownload"] = "Скачать",
        ["Torrent.ButtonStop"] = "Остановить",
        ["Torrent.SpeedPrefix"] = "Скорость:",
        ["Torrent.EtaPrefix"] = "Осталось:",
        ["Torrent.StatusConnecting"] = "Подключение...",
        ["Torrent.StatusActivePeersFormat"] = "Активных пиров: {0} ({1}/{2})",
        ["Torrent.StatusCompleted"] = "Завершено",
        ["Torrent.StatusStopped"] = "Остановлено",
        ["Torrent.StatusErrorFormat"] = "Ошибка: {0}",

        ["File.PiecesUnit"] = "кус.",

        ["Settings.Title"] = "Настройки",
        ["Settings.Header"] = "Параметры папки загрузки",
        ["Settings.SubfolderHint"] = "Для каждого торрента внутри выбранной директории создается отдельная подпапка.",
        ["Settings.OpenFolder"] = "Открыть папку",
        ["Settings.Save"] = "Сохранить",
        ["Settings.Cancel"] = "Отмена",
        ["Settings.DefaultPathFormat"] = "По умолчанию: {0}",
        ["Settings.SelectFolderTitle"] = "Выберите папку для скачивания",
        ["Settings.OpenFolderFailedFormat"] = "Не удалось открыть папку: {0}",
        ["Settings.SaveFailedFormat"] = "Не удалось сохранить настройки: {0}",

        ["Units.Byte"] = "Б",
        ["Units.Kilobyte"] = "КБ",
        ["Units.Megabyte"] = "МБ",
        ["Units.Gigabyte"] = "ГБ",
        ["Units.PerSecond"] = "/с",
        ["Units.Minute"] = "мин",
        ["Units.Hour"] = "ч",
        ["Units.EtaCalculating"] = "расчет (15 с)..."
    };
}
