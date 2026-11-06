namespace SAS_Torrent.Avalonia.Theming;

public interface IThemeService
{
    void ApplyTheme(string themeKey);
    void ApplyDefault();
}