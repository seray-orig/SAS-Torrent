using System;
using System.Globalization;

namespace SAS_Torrent.Avalonia;

public static class SizeFormatter
{
    private const double Kilobyte = 1024d;
    private const double Megabyte = 1024d * 1024d;
    private const double Gigabyte = 1024d * 1024d * 1024d;

    public static string Format(long bytes)
    {
        if (bytes <= 0)
            return "0 Б";
        if (bytes < Kilobyte)
            return $"{bytes} Б";
        if (bytes < Megabyte)
            return $"{(bytes / Kilobyte).ToString("0.##", CultureInfo.CurrentCulture)} КБ";
        if (bytes >= Gigabyte)
            return $"{(bytes / Gigabyte).ToString("0.##", CultureInfo.CurrentCulture)} ГБ";

        return $"{(bytes / Megabyte).ToString("0.##", CultureInfo.CurrentCulture)} МБ";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
            return "0 Б/с";
        return $"{Format((long)bytesPerSecond)}/с";
    }

    public static string FormatEta(TimeSpan? eta)
    {
        if (eta == null)
            return "расчет (15 с)...";

        var value = eta.Value;
        if (value <= TimeSpan.Zero)
            return "0 мин";

        var totalMinutes = (int)Math.Ceiling(value.TotalMinutes);
        if (totalMinutes < 60)
            return $"{Math.Max(1, totalMinutes)} мин";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return minutes > 0
            ? $"{hours} ч {minutes} мин"
            : $"{hours} ч";
    }
}