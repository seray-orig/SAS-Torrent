using System.Runtime.InteropServices;

namespace SAS_Torrent.Avalonia.Localization;

internal static class SystemLanguageDetector
{
    private const int RussianPrimaryLanguageId = 0x19;
    private const ushort PrimaryLanguageIdMask = 0x3FF;

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    public static AppLanguage Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var primaryLanguageId = GetUserDefaultUILanguage() & PrimaryLanguageIdMask;
                return primaryLanguageId == RussianPrimaryLanguageId
                    ? AppLanguage.Russian
                    : AppLanguage.English;
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        return IsRussianUnixLocale()
            ? AppLanguage.Russian
            : AppLanguage.English;
    }

    private static bool IsRussianUnixLocale()
    {
        foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var primaryTag = value.Split('.', '_', '-', '@')[0].Trim();
            if (primaryTag.Equals("ru", StringComparison.OrdinalIgnoreCase) ||
                primaryTag.Equals("russian", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
