using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SAS_Torrent.Avalonia.Theming;

namespace SAS_Torrent.Avalonia;

public partial class App : Application
{
    private IThemeService? _themeService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var themes = new IUiTheme[] { new Win10TaskDialogTheme() };
        _themeService = new AvaloniaThemeService(this, themes, "Win10TaskDialog");
        _themeService.ApplyDefault();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            desktop.MainWindow = new MainWindow(args);
        }
        base.OnFrameworkInitializationCompleted();
    }
}