using System.Globalization;

namespace SAS_Torrent.Avalonia;

public static class SizeFormatter
{
    private const double Megabyte = 1024d * 1024d;
    private const double Gigabyte = 1024d * 1024d * 1024d;

    public static string Format(long bytes)
    {
        if (bytes >= Gigabyte)
            return $"{(bytes / Gigabyte).ToString("0.##", CultureInfo.CurrentCulture)} GB";

        return $"{(bytes / Megabyte).ToString("0.##", CultureInfo.CurrentCulture)} MB";
    }
}
