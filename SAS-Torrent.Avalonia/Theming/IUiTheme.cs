using Avalonia.Styling;

namespace SAS_Torrent.Avalonia.Theming;

public interface IUiTheme
{
    string Key { get; }
    ThemeVariant Variant { get; }
    IStyle BuildStyle();
}