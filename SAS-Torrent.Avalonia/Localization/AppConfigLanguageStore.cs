using SAS_Torrent.Core.Config;

namespace SAS_Torrent.Avalonia.Localization;

public sealed class AppConfigLanguageStore : ILanguagePreferenceStore
{
    private const string RussianValue = "rus";
    private const string EnglishValue = "eng";

    private readonly AppConfig _config;
    private readonly string? _baseDirectory;

    public AppConfigLanguageStore(AppConfig config, string? baseDirectory = null)
    {
        _config = config;
        _baseDirectory = baseDirectory;
    }

    public AppLanguage? LoadPersisted() => _config.UiLanguage switch
    {
        RussianValue => AppLanguage.Russian,
        EnglishValue => AppLanguage.English,
        _ => null
    };

    public void Persist(AppLanguage language)
    {
        _config.UiLanguage = language switch
        {
            AppLanguage.Russian => RussianValue,
            _ => EnglishValue
        };

        try { AppConfig.Save(_config, _baseDirectory); } catch { }
    }
}
