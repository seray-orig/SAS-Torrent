using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SAS_Torrent.Avalonia.Localization;
using SAS_Torrent.Core.Config;

namespace SAS_Torrent.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ILanguageService _language;
    private string? _customPath;
    private Func<string>? _errorTextFactory;

    public SettingsWindow() : this(new AppConfig(), null)
    {
    }

    public SettingsWindow(AppConfig config, ILanguageService? languageService = null)
    {
        InitializeComponent();
        _config = config;
        _language = languageService ?? ResolveLanguageService();
        _language.LanguageChanged += OnLanguageChanged;
        Closed += (s, e) => _language.LanguageChanged -= OnLanguageChanged;
        _customPath = config.CustomDownloadsFolderPath;
        DownloadPathBox.Text = config.ResolveDownloadsFolder();
        RefreshDefaultPathText();
    }

    private static ILanguageService ResolveLanguageService() =>
        Application.Current is App app
            ? app.LanguageService
            : AvaloniaLanguageService.CreateDefault(Application.Current!);

    private void OnLanguageChanged()
    {
        RefreshDefaultPathText();

        if (_errorTextFactory != null)
            ErrorText.Text = _errorTextFactory();
    }

    private void RefreshDefaultPathText()
    {
        DefaultPathText.Text = _language.GetString(
            "Settings.DefaultPathFormat", _config.GetDefaultDownloadsFolder());
    }

    private void SetErrorText(Func<string> textFactory)
    {
        _errorTextFactory = textFactory;
        ErrorText.Text = textFactory();
    }

    private void ClearErrorText()
    {
        _errorTextFactory = null;
        ErrorText.Text = string.Empty;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _language.GetString("Settings.SelectFolderTitle"),
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _customPath = path;
        DownloadPathBox.Text = path;
        ClearErrorText();
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _customPath = null;
        DownloadPathBox.Text = _config.GetDefaultDownloadsFolder();
        ClearErrorText();
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
            SetErrorText(() => _language.GetString("Settings.OpenFolderFailedFormat", ex.Message));
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
            SetErrorText(() => _language.GetString("Settings.SaveFailedFormat", ex.Message));
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
