using System;

namespace PinWins.Interop;

/// <summary>
/// 通过 PrintWindow 抓取窗口画面，并缩放到指定尺寸（BGRA32，自上而下）。
/// 抓取在后台线程执行，避免阻塞界面；调用方负责把字节转换为 Avalonia 位图。
/// </summary>
internal static class WindowThumbnail
{
    private const int MaxSourceEdge = 4096;

    public static byte[]? Capture(IntPtr hwnd, int width, int height)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0)
            return null;

        if (!NativeMethods.IsWindow(hwnd) ||
            !NativeMethods.IsWindowVisible(hwnd) ||
            NativeMethods.IsIconic(hwnd))
        {
            return null;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return null;

        var sourceWidth = Math.Min(rect.Right - rect.Left, MaxSourceEdge);
        var sourceHeight = Math.Min(rect.Bottom - rect.Top, MaxSourceEdge);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            return null;

        var refDc = NativeMethods.GetDC(IntPtr.Zero);
        if (refDc == IntPtr.Zero)
            return null;

        IntPtr sourceDc = IntPtr.Zero, sourceBitmap = IntPtr.Zero, oldSource = IntPtr.Zero;
        IntPtr targetDc = IntPtr.Zero, targetBitmap = IntPtr.Zero, oldTarget = IntPtr.Zero;

        try
        {
            sourceDc = NativeMethods.CreateCompatibleDC(refDc);
            sourceBitmap = NativeMethods.CreateCompatibleBitmap(refDc, sourceWidth, sourceHeight);
            if (sourceDc == IntPtr.Zero || sourceBitmap == IntPtr.Zero)
                return null;

            oldSource = NativeMethods.SelectObject(sourceDc, sourceBitmap);

            if (!NativeMethods.PrintWindow(hwnd, sourceDc, NativeMethods.PW_RENDERFULLCONTENT))
            {
                if (!NativeMethods.PrintWindow(hwnd, sourceDc, 0))
                {
                    // 个别窗口不支持 WM_PRINT，退化为从窗口 DC 直接拷贝（可能包含被遮挡内容）。
                    var windowDc = NativeMethods.GetWindowDC(hwnd);
                    if (windowDc != IntPtr.Zero)
                    {
                        NativeMethods.BitBlt(
                            sourceDc, 0, 0, sourceWidth, sourceHeight,
                            windowDc, 0, 0, NativeMethods.SRCCOPY);
                        NativeMethods.ReleaseDC(hwnd, windowDc);
                    }
                }
            }

            targetDc = NativeMethods.CreateCompatibleDC(refDc);
            targetBitmap = NativeMethods.CreateCompatibleBitmap(refDc, width, height);
            if (targetDc == IntPtr.Zero || targetBitmap == IntPtr.Zero)
                return null;

            oldTarget = NativeMethods.SelectObject(targetDc, targetBitmap);
            NativeMethods.SetStretchBltMode(targetDc, NativeMethods.HALFTONE);

            var scaled = NativeMethods.StretchBlt(
                targetDc, 0, 0, width, height,
                sourceDc, 0, 0, sourceWidth, sourceHeight,
                NativeMethods.SRCCOPY);

            if (!scaled)
                return null;

            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = width,
                    biHeight = -height, // 负数表示自上而下
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                },
            };

            var pixels = new byte[width * height * 4];

            // GetDIBits 要求位图未被选入 DC。
            NativeMethods.SelectObject(targetDc, oldTarget);
            oldTarget = IntPtr.Zero;

            var lines = NativeMethods.GetDIBits(
                targetDc, targetBitmap, 0, (uint)height, pixels, ref info, NativeMethods.DIB_RGB_COLORS);

            if (lines == 0)
                return null;

            // GDI 抓到的位图 alpha 通道通常为 0，直接交给 Avalonia 会被当成全透明，画面发白。
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            return pixels;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (sourceDc != IntPtr.Zero && oldSource != IntPtr.Zero)
                NativeMethods.SelectObject(sourceDc, oldSource);

            if (targetDc != IntPtr.Zero && oldTarget != IntPtr.Zero)
                NativeMethods.SelectObject(targetDc, oldTarget);

            if (sourceBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(sourceBitmap);

            if (targetBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(targetBitmap);

            if (sourceDc != IntPtr.Zero)
                NativeMethods.DeleteDC(sourceDc);

            if (targetDc != IntPtr.Zero)
                NativeMethods.DeleteDC(targetDc);

            NativeMethods.ReleaseDC(IntPtr.Zero, refDc);
        }
    }
}
