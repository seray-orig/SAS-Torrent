namespace SAS_Torrent.Avalonia.Localization;

public interface ILanguagePreferenceStore
{
    AppLanguage? LoadPersisted();

    void Persist(AppLanguage language);
}
