using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SAS_Torrent.Avalonia.Localization;
using SAS_Torrent.Avalonia.Theming;
using SAS_Torrent.Core.Config;

namespace SAS_Torrent.Avalonia;

public partial class App : Application
{
    private IThemeService? _themeService;
    private ILanguageService? _languageService;
    private AppConfig? _config;

    public ILanguageService LanguageService => _languageService ??= AvaloniaLanguageService.CreateDefault(this);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var themes = new IUiTheme[] { new Win10TaskDialogTheme() };
        _themeService = new AvaloniaThemeService(this, themes, "Win10TaskDialog");
        _themeService.ApplyDefault();

        try { _config = AppConfig.EnsureCreated(); }
        catch { _config = new AppConfig(); }

        _languageService = AvaloniaLanguageService.CreateDefault(this, new AppConfigLanguageStore(_config));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            desktop.MainWindow = new MainWindow(args, LanguageService, _config);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
