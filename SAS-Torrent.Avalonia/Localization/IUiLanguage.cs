namespace SAS_Torrent.Avalonia.Localization;

public interface IUiLanguage
{
    AppLanguage Language { get; }

    IReadOnlyDictionary<string, string> Strings { get; }
}
