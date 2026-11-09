using Avalonia;

namespace SAS_Torrent.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        NativeLibraries.EnsureLoaded();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
