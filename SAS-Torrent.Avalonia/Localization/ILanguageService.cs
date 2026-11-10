namespace SAS_Torrent.Avalonia.Localization;

public interface ILanguageService
{
    AppLanguage Current { get; }

    event Action? LanguageChanged;

    void ApplySystemLanguage();
    void ApplyLanguage(AppLanguage language);
    void ToggleLanguage();
    string GetString(string key, params object[] args);
}
