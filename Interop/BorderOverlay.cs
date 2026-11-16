using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PinWins.Core;

namespace PinWins.Interop;

/// <summary>
/// 为目标窗口绘制一圈“空心”彩色描边。
///
/// 为什么不再只用 DWM 边框色：
/// DWMWA_BORDER_COLOR 固定 1px、只在 Win11 的标准边框窗口上可见，Electron 等合成窗口在失活后会变淡/消失，
/// WPF（自定义 WindowChrome）窗口则完全不显示。这里改为独立的分层窗口：
///   · 尺寸 = 目标窗口外扩一圈，用 SetWindowRgn 挖空中间，只保留环形区域；
///   · WS_EX_LAYERED | WS_EX_TRANSPARENT 保证鼠标完全穿透，不影响宿主拖动/点击；
///   · WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE 保证不出现在任务栏/Alt+Tab 且永不抢焦点；
///   · 通过 SetWinEventHook 跟随目标移动/缩放/最小化；另外 PinManager 会定时校对位置，
///     防止个别窗口（WPF/Electron）漏发位置事件时描边与窗体错位。
/// </summary>
internal sealed class BorderOverlay : IDisposable
{
    private const string ClassName = "PinWins.BorderOverlay.Class";

    public const int MinWidth = 1;
    public const int MaxWidth = 10;

    private static NativeMethods.WndProcDelegate? _wndProc;
    private static NativeMethods.WinEventProc? _winEventProc;

    /// <summary>hwnd -> 实例，供静态 WndProc / WinEvent 回调分发。</summary>
    private static readonly Dictionary<IntPtr, BorderOverlay> Instances = new();

    private readonly IntPtr _target;
    private readonly IntPtr _hwnd;

    private IntPtr _hookLocation;
    private IntPtr _hookMinimize;

    private IntPtr _brush;
    private uint _colorRef;
    private int _width;

    private bool _visible;
    private bool _disposed;

    private int _lastWidth = -1;
    private int _lastHeight = -1;
    private int _lastThickness = -1;
    private int _lastLeft;
    private int _lastTop;

    public BorderOverlay(IntPtr target, uint colorRef, int width)
    {
        _target = target;
        _colorRef = colorRef;
        _width = ClampWidth(width);
        _brush = NativeMethods.CreateSolidBrush(colorRef);

        RegisterWindowClass();

        _hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST,
            ClassName,
            "PinWins.BorderOverlay",
            NativeMethods.WS_POPUP,
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("创建边框覆盖窗口失败。");

        Instances[_hwnd] = this;

        // 分层窗口必须设置透明度，否则内容可能不显示。
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);

        InstallHooks();
        Reposition();
    }

    public static int ClampWidth(int width) => Math.Clamp(width, MinWidth, MaxWidth);

    /// <summary>更新描边颜色/宽度（宽度变化时会重建环形区域）。</summary>
    public void Update(uint colorRef, int width)
    {
        if (_disposed)
            return;

        var clamped = ClampWidth(width);

        if (colorRef != _colorRef)
        {
            _colorRef = colorRef;
            var old = _brush;
            _brush = NativeMethods.CreateSolidBrush(colorRef);
            if (old != IntPtr.Zero)
                NativeMethods.DeleteObject(old);

            NativeMethods.InvalidateRect(_hwnd, IntPtr.Zero, false);
        }

        if (clamped != _width)
        {
            _width = clamped;
            _lastThickness = -1;
        }

        Reposition();
    }

    /// <summary>按目标窗口当前位置重新对齐描边；目标不可见/最小化时隐藏。</summary>
    public void Reposition()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
            return;

        try
        {
            if (!NativeMethods.IsWindow(_target) ||
                !NativeMethods.IsWindowVisible(_target) ||
                NativeMethods.IsIconic(_target))
            {
                Hide();
                return;
            }

            var bounds = GetTargetBounds(_target);
            if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            {
                Hide();
                return;
            }

            var thickness = _width;
            var width = bounds.Right - bounds.Left + thickness * 2;
            var height = bounds.Bottom - bounds.Top + thickness * 2;
            if (width <= 0 || height <= 0)
            {
                Hide();
                return;
            }

            var sizeChanged = width != _lastWidth || height != _lastHeight || thickness != _lastThickness;
            if (sizeChanged)
            {
                if (!ApplyRingRegion(width, height, thickness))
                    return;

                _lastWidth = width;
                _lastHeight = height;
                _lastThickness = thickness;
            }

            var left = bounds.Left - thickness;
            var top = bounds.Top - thickness;

            // 位置/尺寸都没变、且描边仍然紧贴在目标窗口正下方时直接返回，避免定时校对时反复 SetWindowPos。
            // 注意两点：
            //   · 必须把尺寸一起比较：缩放窗口时左上角通常不变，只比较位置会漏掉尺寸变化；
            //   · 必须校验 z 序：切换窗口时系统只会抬高目标窗口，描边会留在后面被别的置顶窗口盖住，
            //     表现为“边框分层错位”，所以 z 序不对时即使位置没变也要重新插入。
            var moved = sizeChanged || !_visible || left != _lastLeft || top != _lastTop;
            var zOrderOk = NativeMethods.GetWindow(_hwnd, NativeMethods.GW_HWNDPREV) == _target;

            if (!moved && zOrderOk)
                return;

            // 把描边插到目标窗口正下方（而不是无条件置顶）：
            // 这样当有别的窗口盖住目标时，描边也会被一起盖住，不会出现“边框飘在其它窗口之上”的层次错乱。
            var flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
            if (!moved)
                flags |= NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;

            NativeMethods.SetWindowPos(
                _hwnd,
                _target,
                left,
                top,
                width,
                height,
                flags);

            _lastLeft = left;
            _lastTop = top;
            _visible = true;
        }
        catch (Exception ex)
        {
            Logger.Error("BorderOverlay.Reposition 异常", ex);
        }
    }

    private void Hide()
    {
        if (!_visible || _disposed)
            return;

        _visible = false;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    /// <summary>外矩形减去内矩形，得到只有边框的环形区域。</summary>
    private bool ApplyRingRegion(int width, int height, int thickness)
    {
        var outer = NativeMethods.CreateRectRgn(0, 0, width, height);
        var inner = NativeMethods.CreateRectRgn(thickness, thickness, width - thickness, height - thickness);
        var ring = NativeMethods.CreateRectRgn(0, 0, 0, 0);

        try
        {
            if (outer == IntPtr.Zero || inner == IntPtr.Zero || ring == IntPtr.Zero)
                return false;

            if (NativeMethods.CombineRgn(ring, outer, inner, NativeMethods.RGN_DIFF) == 0)
                return false;

            // 交给系统接管 ring 的所有权，之后不能再 DeleteObject。
            return NativeMethods.SetWindowRgn(_hwnd, ring, true) != 0;
        }
        finally
        {
            if (outer != IntPtr.Zero)
                NativeMethods.DeleteObject(outer);

            if (inner != IntPtr.Zero)
                NativeMethods.DeleteObject(inner);
        }
    }

    private static NativeMethods.RECT GetTargetBounds(IntPtr target)
    {
        // EXTENDED_FRAME_BOUNDS 是用户真正看到的窗口边界（不含 DWM 阴影），比 GetWindowRect 更贴合。
        var hr = NativeMethods.DwmGetWindowAttributeRect(
            target,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out var rect,
            Marshal.SizeOf<NativeMethods.RECT>());

        if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
            return rect;

        NativeMethods.GetWindowRect(target, out rect);
        return rect;
    }

    // ---- WinEvent 跟随 ----------------------------------------------------

    private void InstallHooks()
    {
        _winEventProc ??= OnWinEvent;

        var threadId = NativeMethods.GetWindowThreadProcessId(_target, out var processId);
        if (threadId == 0)
            return;

        _hookLocation = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _winEventProc,
            processId,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        _hookMinimize = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _winEventProc,
            processId,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private static void OnWinEvent(
        IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE && idObject != NativeMethods.OBJID_WINDOW)
                return;

            // Instances 可能被 Dispose 修改，这里复制一份键避免遍历时异常。
            foreach (var overlay in SnapshotInstances())
            {
                if (overlay._disposed || !overlay.Matches(hwnd))
                    continue;

                if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
                    overlay.Hide();
                else
                    overlay.Reposition();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("BorderOverlay.OnWinEvent 异常", ex);
        }
    }

    private static BorderOverlay[] SnapshotInstances()
    {
        if (Instances.Count == 0)
            return Array.Empty<BorderOverlay>();

        var array = new BorderOverlay[Instances.Count];
        Instances.Values.CopyTo(array, 0);
        return array;
    }

    private bool Matches(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        if (hwnd == _target)
            return true;

        // 部分应用（含 Electron）只上报子窗口的位置变化。
        return NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) == _target;
    }

    // ---- 窗口类 / 绘制 ----------------------------------------------------

    private static void RegisterWindowClass()
    {
        if (_wndProc != null)
            return;

        _wndProc = WndProc;

        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            hbrBackground = IntPtr.Zero,
            lpszClassName = ClassName,
        };

        NativeMethods.RegisterClassEx(ref wc);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    return new IntPtr(NativeMethods.HTTRANSPARENT);

                case NativeMethods.WM_ERASEBKGND:
                    return new IntPtr(1);

                case NativeMethods.WM_PAINT:
                    Paint(hWnd);
                    return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Logger.Error($"BorderOverlay.WndProc 异常 (msg=0x{msg:X4})", ex);
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    /// <summary>整块填充颜色，区域的裁剪保证只有边框区域可见。</summary>
    private static void Paint(IntPtr hwnd)
    {
        if (!Instances.TryGetValue(hwnd, out var self))
            return;

        var hdc = NativeMethods.BeginPaint(hwnd, out var ps);
        if (hdc == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.GetClientRect(hwnd, out var rect);
            NativeMethods.FillRect(hdc, ref rect, self._brush);
        }
        finally
        {
            NativeMethods.EndPaint(hwnd, ref ps);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Instances.Remove(_hwnd);

        try
        {
            if (_hookLocation != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(_hookLocation);

            if (_hookMinimize != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(_hookMinimize);
        }
        catch (Exception ex)
        {
            Logger.Error("BorderOverlay 取消 WinEvent 钩子异常", ex);
        }

        _hookLocation = IntPtr.Zero;
        _hookMinimize = IntPtr.Zero;

        try
        {
            if (_hwnd != IntPtr.Zero)
                NativeMethods.DestroyWindow(_hwnd);
        }
        catch (Exception ex)
        {
            Logger.Error("BorderOverlay 销毁窗口异常", ex);
        }

        if (_brush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_brush);
            _brush = IntPtr.Zero;
        }
    }
}
