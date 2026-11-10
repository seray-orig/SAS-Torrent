using Avalonia;
using Avalonia.Controls;

namespace SAS_Torrent.Avalonia.Localization;

public sealed class AvaloniaLanguageService : ILanguageService
{
    private readonly Application _application;
    private readonly IReadOnlyDictionary<AppLanguage, IUiLanguage> _languages;
    private readonly Func<AppLanguage> _systemLanguageDetector;
    private readonly ILanguagePreferenceStore? _preferenceStore;
    private IResourceDictionary? _activeResources;

    public event Action? LanguageChanged;

    public AppLanguage Current { get; private set; }

    public AvaloniaLanguageService(
        Application application,
        IEnumerable<IUiLanguage> languages,
        Func<AppLanguage>? systemLanguageDetector = null,
        ILanguagePreferenceStore? preferenceStore = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));

        var registered = languages?.ToList() ?? throw new ArgumentNullException(nameof(languages));
        if (registered.Count == 0)
            throw new ArgumentException("At least one language must be registered.", nameof(languages));

        _languages = registered.ToDictionary(language => language.Language);
        Current = registered[0].Language;
        _systemLanguageDetector = systemLanguageDetector ?? SystemLanguageDetector.Detect;
        _preferenceStore = preferenceStore;
    }

    public static ILanguageService CreateDefault(Application application, ILanguagePreferenceStore? preferenceStore = null)
    {
        var service = new AvaloniaLanguageService(
            application,
            new IUiLanguage[] { new RussianLanguage(), new EnglishLanguage() },
            preferenceStore: preferenceStore);

        service.ApplyInitialLanguage();
        return service;
    }

    public void ApplySystemLanguage() => ApplyLanguageCore(_systemLanguageDetector(), persist: false);

    public void ApplyLanguage(AppLanguage language) => ApplyLanguageCore(language, persist: true);

    public void ToggleLanguage() =>
        ApplyLanguage(Current == AppLanguage.Russian ? AppLanguage.English : AppLanguage.Russian);

    public string GetString(string key, params object[] args)
    {
        var value = _languages[Current].Strings.TryGetValue(key, out var text) ? text : key;
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    private void ApplyInitialLanguage()
    {
        var persisted = _preferenceStore?.LoadPersisted();
        ApplyLanguageCore(persisted ?? _systemLanguageDetector(), persist: false);
    }

    private void ApplyLanguageCore(AppLanguage language, bool persist)
    {
        if (!_languages.TryGetValue(language, out var uiLanguage))
            throw new InvalidOperationException($"Language '{language}' is not registered.");

        var resources = BuildResources(uiLanguage);
        SwapActiveResources(resources);

        Current = language;

        if (persist)
            _preferenceStore?.Persist(language);

        LanguageChanged?.Invoke();
    }

    private void SwapActiveResources(IResourceDictionary resources)
    {
        var dictionaries = _application.Resources.MergedDictionaries;

        if (_activeResources != null)
        {
            var index = dictionaries.IndexOf(_activeResources);
            if (index >= 0)
            {
                dictionaries[index] = resources;
                _activeResources = resources;
                return;
            }
        }

        dictionaries.Add(resources);
        _activeResources = resources;
    }

    private static IResourceDictionary BuildResources(IUiLanguage language)
    {
        var resources = new ResourceDictionary();
        foreach (var (key, value) in language.Strings)
            resources.Add(key, value);
        return resources;
    }
}
