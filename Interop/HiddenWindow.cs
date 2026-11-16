using System;
using PinWins.Core;

namespace PinWins.Interop;

/// <summary>
/// 一个不可见、不显示在任务栏/Alt+Tab 之中的顶层消息窗口。
/// 用于接收全局热键与托盘图标的消息。
/// </summary>
internal sealed class HiddenWindow : IDisposable
{
    public const uint TrayCallbackMessage = NativeMethods.WM_APP + 1;

    private const string ClassName = "PinWins.MessageWindow.Class";

    private static NativeMethods.WndProcDelegate? _wndProc;
    private static HiddenWindow? _instance;

    /// <summary>资源管理器重启后广播的 "TaskbarCreated" 消息（值运行时分配）。</summary>
    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    public IntPtr Handle { get; private set; }

    public event Action? HotkeyPressed;
    public event Action? TrayLeftClick;
    public event Action? TrayRightClick;

    /// <summary>资源管理器（任务栏）重启，托盘图标需要重新添加。</summary>
    public event Action? TaskbarCreated;

    /// <summary>另一个程序实例请求本实例显示设置窗口。</summary>
    public event Action? ShowRequested;

    public HiddenWindow()
    {
        RegisterClass();

        var hInstance = NativeMethods.GetModuleHandle(null);
        Handle = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            ClassName,
            "PinWins",
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException("创建消息窗口失败。");

        _instance = this;
    }

    private static void RegisterClass()
    {
        if (_wndProc != null)
            return;

        _wndProc = WndProc;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            hCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW)),
            hbrBackground = IntPtr.Zero,
            lpszClassName = ClassName,
        };

        NativeMethods.RegisterClassEx(ref wc);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var self = _instance;
            if (self != null)
            {
                if (TaskbarCreatedMessage != 0 && msg == TaskbarCreatedMessage)
                {
                    self.TaskbarCreated?.Invoke();
                    return IntPtr.Zero;
                }

                switch (msg)
                {
                    case NativeMethods.WM_HOTKEY:
                        self.HotkeyPressed?.Invoke();
                        return IntPtr.Zero;

                    case NativeMethods.WM_APP_SHOW_SETTINGS:
                        self.ShowRequested?.Invoke();
                        return IntPtr.Zero;

                    case TrayCallbackMessage:
                    {
                        var evt = (uint)(lParam.ToInt64() & 0xFFFF);
                        // 一次左键会同时产生 NIN_SELECT 与 WM_LBUTTONUP，这里只响应一处，
                        // 去重逻辑由 AppController 进一步保证。
                        if (evt is NativeMethods.NIN_SELECT or NativeMethods.WM_LBUTTONUP)
                            self.TrayLeftClick?.Invoke();
                        else if (evt == NativeMethods.WM_RBUTTONUP)
                            self.TrayRightClick?.Invoke();
                        return IntPtr.Zero;
                    }
                }
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Logger.Error($"HiddenWindow.WndProc 异常 (msg=0x{msg:X4})", ex);
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
        }

        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
