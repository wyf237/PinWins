using Avalonia.Media;

namespace PinWins.Core;

internal static class ColorUtil
{
    public static Color Parse(string? hex, Color fallback)
        => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : fallback;

    /// <summary>转换为 Win32 COLORREF(0x00BBGGRR)。</summary>
    public static uint ToColorRef(Color c)
        => (uint)(c.R | (c.G << 8) | (c.B << 16));

    public static string ToHex(Color c)
        => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
