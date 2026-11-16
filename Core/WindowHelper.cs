using System;
using System.Collections.Generic;
using System.Text;
using PinWins.Interop;

namespace PinWins.Core;

internal static class WindowHelper
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_DesktopShellWindow",
    };

    /// <summary>返回鼠标指针下方的顶层窗口句柄，找不到或为桌面/任务栏时返回 IntPtr.Zero。</summary>
    public static IntPtr GetWindowUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
            return IntPtr.Zero;

        var hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;

        return root;
    }

    public static string GetClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var len = NativeMethods.GetWindowTextLength(hwnd);
        var sb = new StringBuilder(Math.Max(2, len + 2));
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsIgnored(IntPtr hwnd)
        => hwnd == IntPtr.Zero || IgnoredClasses.Contains(GetClassName(hwnd));

    /// <summary>窗口是否属于本进程（设置窗口、缩略图面板等，不应被置顶）。</summary>
    public static bool IsOwnProcessWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            hwnd,
            topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>把窗口恢复/唤起并激活到最前（缩略图点击时使用）。</summary>
    public static void Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return;

        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        else
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);

        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.SetForegroundWindow(hwnd);
    }
}
