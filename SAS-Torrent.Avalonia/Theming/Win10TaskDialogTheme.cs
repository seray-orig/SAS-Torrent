using Avalonia.Styling;

namespace SAS_Torrent.Avalonia.Theming;

public sealed class Win10TaskDialogTheme : IUiTheme
{
    public string Key => "Win10TaskDialog";

    public ThemeVariant Variant => ThemeVariant.Light;

    public IStyle BuildStyle()
    {
        return new Win10TaskDialogStyles();
    }
}