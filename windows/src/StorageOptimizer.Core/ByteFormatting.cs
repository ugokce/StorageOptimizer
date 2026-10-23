using System.Globalization;

namespace StorageOptimizer.Core;

public static class ByteFormatting
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a byte count the way File Explorer does (1 KB = 1,024 bytes), e.g. "1.23 GB".</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{Math.Max(bytes, 0).ToString("N0", CultureInfo.CurrentCulture)} bytes";
        }

        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {Units[unit]}";
    }
}
