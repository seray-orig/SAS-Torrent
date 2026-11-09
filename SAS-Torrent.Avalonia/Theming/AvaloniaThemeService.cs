using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Styling;

namespace SAS_Torrent.Avalonia.Theming;

public sealed class AvaloniaThemeService : IThemeService
{
    private readonly Application _application;
    private readonly IReadOnlyDictionary<string, IUiTheme> _themes;
    private readonly string _defaultKey;
    private IStyle? _activeStyle;

    public AvaloniaThemeService(Application application, IEnumerable<IUiTheme> themes, string defaultKey)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _themes = themes.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        _defaultKey = defaultKey;
    }

    public void ApplyDefault() => ApplyTheme(_defaultKey);

    public void ApplyTheme(string themeKey)
    {
        if (!_themes.TryGetValue(themeKey, out var theme))
            throw new InvalidOperationException($"Theme '{themeKey}' is not registered.");

        if (_activeStyle != null)
            _application.Styles.Remove(_activeStyle);

        _application.RequestedThemeVariant = theme.Variant;
        _activeStyle = theme.BuildStyle();
        _application.Styles.Add(_activeStyle);
    }
}