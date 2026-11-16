using System;
using System.IO;
using System.Runtime.InteropServices;
using PinWins.Interop;

namespace PinWins.Services;

/// <summary>
/// 系统托盘图标（通过 Shell_NotifyIcon 实现，避免引入额外依赖）。
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint IconId = 1101;

    private readonly IntPtr _hwnd;
    private readonly string _iconPath;
    private readonly string _tip;

    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _added;

    public TrayIconService(IntPtr hwnd, string iconPath, string tip)
    {
        _hwnd = hwnd;
        _iconPath = iconPath;
        _tip = tip;
    }

    public bool IsVisible => _added;

    public bool Show()
    {
        if (_icon == IntPtr.Zero)
            _icon = LoadIconHandle();

        var data = BuildData();
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        return _added;
    }

    /// <summary>资源管理器重启后图标会丢失，这里重新添加一次。</summary>
    public void Refresh()
    {
        _added = false;
        Show();
    }

    private IntPtr LoadIconHandle()
    {
        if (File.Exists(_iconPath))
        {
            var handle = NativeMethods.LoadImage(
                IntPtr.Zero, _iconPath, NativeMethods.IMAGE_ICON, 16, 16, NativeMethods.LR_LOADFROMFILE);
            if (handle != IntPtr.Zero)
            {
                _ownsIcon = true;
                return handle;
            }
        }

        _ownsIcon = false;
        return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(NativeMethods.IDI_APPLICATION));
    }

    private NativeMethods.NOTIFYICONDATA BuildData() => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = IconId,
        uCallbackMessage = HiddenWindow.TrayCallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData();
            data.uFlags = 0;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero && _ownsIcon)
        {
            NativeMethods.DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
    }
}
