using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SAS_Torrent.Avalonia;

internal static class NativeLibraries
{
    private const string ResourcePrefix = "SAS_Torrent.Avalonia.Native.";
    
    private static readonly string[] LoadOrder = GetLoadOrder();

    private static string[] GetLoadOrder()
    {
        if (OperatingSystem.IsWindows())
            return new[] { "libHarfBuzzSharp.dll", "libSkiaSharp.dll", "av_libglesv2.dll" };
            
        if (OperatingSystem.IsMacOS())
            return new[] { "libHarfBuzzSharp.dylib", "libSkiaSharp.dylib", "libAvaloniaNative.dylib" };
            
        return new[] { "libHarfBuzzSharp.so", "libSkiaSharp.so", "libAvaloniaNative.so" };
    }

    public static void EnsureLoaded()
    {
        var assembly = typeof(NativeLibraries).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
            return;

        var payloads = new List<(string FileName, byte[] Data)>(resources.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null)
                continue;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
            hash.AppendData(data);
            
            payloads.Add((resource[ResourcePrefix.Length..], data));
        }

        if (payloads.Count == 0)
            return;

        var hashText = Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SAS-Torrent",
            "native",
            hashText);

        Directory.CreateDirectory(directory);
        Extract(payloads, directory);

        if (OperatingSystem.IsWindows())
            SetDllDirectory(directory);

        foreach (var fileName in LoadOrder)
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
                NativeLibrary.Load(path);
        }
    }

    private static void Extract(List<(string FileName, byte[] Data)> payloads, string directory)
    {
        foreach (var (fileName, data) in payloads)
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path) && new FileInfo(path).Length == data.Length)
                continue;

            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, true);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);
}