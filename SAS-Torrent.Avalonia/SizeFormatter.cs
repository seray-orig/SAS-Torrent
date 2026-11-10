using System;
using System.Globalization;
using SAS_Torrent.Avalonia.Localization;

namespace SAS_Torrent.Avalonia;

public static class SizeFormatter
{
    private const double Kilobyte = 1024d;
    private const double Megabyte = 1024d * 1024d;
    private const double Gigabyte = 1024d * 1024d * 1024d;

    public static string Format(long bytes, ILanguageService language)
    {
        if (bytes <= 0)
            return $"0 {language.GetString("Units.Byte")}";
        if (bytes < Kilobyte)
            return $"{bytes} {language.GetString("Units.Byte")}";
        if (bytes < Megabyte)
            return $"{(bytes / Kilobyte).ToString("0.##", CultureInfo.CurrentCulture)} {language.GetString("Units.Kilobyte")}";
        if (bytes >= Gigabyte)
            return $"{(bytes / Gigabyte).ToString("0.##", CultureInfo.CurrentCulture)} {language.GetString("Units.Gigabyte")}";

        return $"{(bytes / Megabyte).ToString("0.##", CultureInfo.CurrentCulture)} {language.GetString("Units.Megabyte")}";
    }

    public static string FormatSpeed(double bytesPerSecond, ILanguageService language)
    {
        if (bytesPerSecond <= 0)
            return $"0 {language.GetString("Units.Byte")}{language.GetString("Units.PerSecond")}";
        return $"{Format((long)bytesPerSecond, language)}{language.GetString("Units.PerSecond")}";
    }

    public static string FormatEta(TimeSpan? eta, ILanguageService language)
    {
        if (eta == null)
            return language.GetString("Units.EtaCalculating");

        var value = eta.Value;
        if (value <= TimeSpan.Zero)
            return $"0 {language.GetString("Units.Minute")}";

        var totalMinutes = (int)Math.Ceiling(value.TotalMinutes);
        if (totalMinutes < 60)
            return $"{Math.Max(1, totalMinutes)} {language.GetString("Units.Minute")}";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return minutes > 0
            ? $"{hours} {language.GetString("Units.Hour")} {minutes} {language.GetString("Units.Minute")}"
            : $"{hours} {language.GetString("Units.Hour")}";
    }
}
