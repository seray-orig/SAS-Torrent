using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SAS_Torrent.Core.Config;

namespace SAS_Torrent.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private string? _customPath;

    public SettingsWindow() : this(new AppConfig())
    {
    }

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        _customPath = config.CustomDownloadsFolderPath;
        DownloadPathBox.Text = config.ResolveDownloadsFolder();
        DefaultPathText.Text = $"По умолчанию: {config.GetDefaultDownloadsFolder()}";
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для скачивания",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _customPath = path;
        DownloadPathBox.Text = path;
        ErrorText.Text = string.Empty;
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _customPath = null;
        DownloadPathBox.Text = _config.GetDefaultDownloadsFolder();
        ErrorText.Text = string.Empty;
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var folder = DownloadPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
            folder = _config.GetDefaultDownloadsFolder();

        try
        {
            Directory.CreateDirectory(folder);
            OpenFolder(folder);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _config.CustomDownloadsFolderPath = _customPath;

        try
        {
            AppConfig.Save(_config);
            Close(true);
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private static void OpenFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", folder);
        }
        else
        {
            Process.Start("xdg-open", folder);
        }
    }
}
