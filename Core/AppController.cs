using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using PinWins.Interop;
using PinWins.Models;
using PinWins.Services;
using PinWins.Views;

namespace PinWins.Core;

/// <summary>
/// 应用主控制器：串联配置、热键、托盘与窗口置顶逻辑。
/// </summary>
public sealed class AppController
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    private readonly PinManager _pinManager = new();
    private HiddenWindow? _messageWindow;
    private GlobalHotkeyService? _hotkeys;
    private TrayIconService? _tray;
    private SettingsWindow? _settingsWindow;
    private PinnedWindowsWindow? _pinnedWindowsWindow;
    private bool _exiting;
    private long _lastTrayClickTicks;

    private NativeMethods.WinEventProc? _foregroundProc;
    private IntPtr _foregroundHook;
    private IntPtr _foregroundHandle;

    public AppController(IClassicDesktopStyleApplicationLifetime lifetime)
        => _lifetime = lifetime;

    public AppSettings Settings { get; private set; } = new();

    public bool IsExiting => _exiting;

    public string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    public IntPtr MessageWindowHandle => _messageWindow?.Handle ?? IntPtr.Zero;

    /// <summary>最近一次被激活的外部窗口（不含本程序自己的窗口），用于预览面板标记当前窗口。</summary>
    public IntPtr ActiveWindowHandle => _foregroundHandle;

    public void Start()
    {
        Logger.Info("AppController.Start 开始");
        Settings = SettingsService.Load();
        Logger.Info($"配置加载完成: HasConfigured={Settings.HasConfigured}, Hotkey={Settings.ToBinding()}");

        StartForegroundTracking();

        _messageWindow = new HiddenWindow();
        _messageWindow.HotkeyPressed += OnHotkeyPressed;
        _messageWindow.TrayLeftClick += OnTrayLeftClick;
        _messageWindow.TrayRightClick += ShowTrayMenu;
        _messageWindow.ShowRequested += () => ShowSettings();
        _messageWindow.TaskbarCreated += OnTaskbarCreated;

        _hotkeys = new GlobalHotkeyService(_messageWindow.Handle);

        _pinManager.Configure(Settings.MultiWindow, Settings.BorderColor, Settings.BorderWidth);
        _pinManager.Start();

        // 托盘常驻：只要程序在运行就显示托盘图标，与是否配置热键、是否有关闭窗口无关。
        try
        {
            EnsureTray();
            Logger.Info("托盘显示完成");
        }
        catch (Exception ex)
        {
            Logger.Error("EnsureTray 异常", ex);
        }

        // 没有默认快捷键：只注册用户自己设置过的那一个。
        var binding = Settings.ToBinding();
        if (binding.IsValid)
        {
            if (!_hotkeys.Register(binding))
            {
                Logger.Info("热键注册失败，打开设置窗口");
                ShowSettings("保存的快捷键注册失败（可能已被占用），请重新设置。");
            }
        }
        else
        {
            Logger.Info("尚未设置快捷键，打开设置窗口");
            ShowSettings();
        }

        Logger.Info("AppController.Start 完成");
    }

    public void ShowSettings(string? error = null)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += OnSettingsClosed;
        }

        _settingsWindow.Present(error);
    }

    private void OnSettingsClosed(object? sender, EventArgs e)
    {
        Logger.Info($"设置窗口关闭, IsExiting={_exiting}");
        _settingsWindow = null;

        // 关闭设置窗口不退出程序：程序常驻托盘，只有托盘菜单的“退出”才会结束进程。
    }

    private void OnHotkeyPressed()
    {
        try
        {
            Logger.Info("热键触发");
            var target = WindowHelper.GetWindowUnderCursor();
            if (target == IntPtr.Zero)
            {
                Logger.Info("未找到鼠标下的窗口");
                return;
            }

            Logger.Info($"目标窗口: 0x{target.ToInt64():X}");

            if (WindowHelper.IsIgnored(target))
            {
                Logger.Info("窗口被忽略");
                return;
            }

            _pinManager.Toggle(target);
            Logger.Info("Toggle 完成");
        }
        catch (Exception ex)
        {
            Logger.Error("AppController.OnHotkeyPressed 异常", ex);
        }
    }

    /// <summary>
    /// 托盘左键：一次点击可能同时触发 NIN_SELECT 与 WM_LBUTTONUP 等多个回调，
    /// 这里做去重，避免重复弹窗或打开多个设置窗口。
    /// </summary>
    private void OnTrayLeftClick()
    {
        var now = Environment.TickCount64;
        if (now - _lastTrayClickTicks < 500)
            return;

        _lastTrayClickTicks = now;
        ShowSettings();
    }

    /// <summary>资源管理器重启后重新添加托盘图标，避免图标“莫名消失”。</summary>
    private void OnTaskbarCreated()
    {
        try
        {
            Logger.Info("收到 TaskbarCreated，重新添加托盘图标");
            _tray?.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error("恢复托盘图标异常", ex);
        }
    }

    /// <summary>校验并保存配置，成功返回 true。</summary>
    public bool TryApplySettings(
        HotkeyBinding binding, string borderColorHex, bool multiWindow, int borderWidth, out string? error)
    {
        error = null;

        if (binding is null || !binding.IsValid)
        {
            error = "请先设置一个包含修饰键（Ctrl / Alt / Shift / Win）的全局快捷键。";
            return false;
        }

        if (_hotkeys is null)
        {
            error = "内部错误：热键服务未初始化。";
            return false;
        }

        // 先尝试注册新热键；若失败则恢复旧热键，避免程序失去响应。
        var oldBinding = Settings.ToBinding();
        _hotkeys.Unregister();

        if (!_hotkeys.Register(binding))
        {
            if (oldBinding.IsValid)
                _hotkeys.Register(oldBinding);

            error = "该快捷键注册失败，可能已被其它程序占用，请更换后重试。";
            return false;
        }

        Settings.HotkeyModifiers = binding.Modifiers;
        Settings.HotkeyVirtualKey = binding.VirtualKey;
        Settings.BorderColor = borderColorHex;
        Settings.BorderWidth = BorderOverlay.ClampWidth(borderWidth);
        Settings.MultiWindow = multiWindow;
        Settings.HasConfigured = true;

        SettingsService.Save(Settings);
        _pinManager.Configure(multiWindow, borderColorHex, Settings.BorderWidth);
        EnsureTray();
        return true;
    }

    // ---- 已置顶窗口缩略图 -------------------------------------------------

    public IReadOnlyList<PinnedWindowInfo> GetPinnedWindows() => _pinManager.GetPinnedWindows();

    /// <summary>取消指定窗口的置顶（缩略图窗口的关闭按钮）。</summary>
    public void UnpinWindow(IntPtr handle) => _pinManager.Unpin(handle);

    /// <summary>把指定窗口恢复并激活到最前（点击缩略图）。</summary>
    public void ActivateWindow(IntPtr handle)
    {
        try
        {
            WindowHelper.Activate(handle);
        }
        catch (Exception ex)
        {
            Logger.Error("激活窗口异常", ex);
        }
    }

    public void ShowPinnedWindows()
    {
        try
        {
            if (_pinnedWindowsWindow == null)
            {
                _pinnedWindowsWindow = new PinnedWindowsWindow(this);
                _pinnedWindowsWindow.Closed += (_, _) => _pinnedWindowsWindow = null;
            }

            _pinnedWindowsWindow.Present();
        }
        catch (Exception ex)
        {
            Logger.Error("打开已置顶窗口预览异常", ex);
        }
    }

    /// <summary>
    /// 跟踪前台窗口：预览面板自己获得焦点时，仍需要知道上一个被激活的外部窗口，
    /// WINEVENT_SKIPOWNPROCESS 保证本程序自己的窗口不会覆盖记录。
    /// </summary>
    private void StartForegroundTracking()
    {
        try
        {
            var current = NativeMethods.GetForegroundWindow();
            if (!WindowHelper.IsOwnProcessWindow(current))
                _foregroundHandle = current;

            _foregroundProc ??= (hook, eventType, hwnd, idObject, idChild, thread, time) =>
            {
                try
                {
                    if (idObject == NativeMethods.OBJID_WINDOW && hwnd != IntPtr.Zero)
                        _foregroundHandle = hwnd;
                }
                catch (Exception ex)
                {
                    Logger.Error("前台窗口跟踪异常", ex);
                }
            };

            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        }
        catch (Exception ex)
        {
            Logger.Error("安装前台窗口钩子异常", ex);
        }
    }

    private void EnsureTray()
    {
        if (_tray != null || _messageWindow == null)
            return;

        _tray = new TrayIconService(_messageWindow.Handle, IconPath, "PinWins - 窗口置顶工具");
        _tray.Show();
    }

    private void ShowTrayMenu()
    {
        var hwnd = MessageWindowHandle;
        if (hwnd == IntPtr.Zero)
            return;

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(1), "设置(&S)...");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(2), "取消全部置顶(&U)");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(3), "已置顶窗口(&P)...");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, IntPtr.Zero, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new IntPtr(4), "退出(&X)");

            NativeMethods.GetCursorPos(out var pt);
            NativeMethods.SetForegroundWindow(hwnd);

            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, hwnd, IntPtr.Zero);

            NativeMethods.PostMessage(hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case 1:
                    ShowSettings();
                    break;
                case 2:
                    _pinManager.ClearAll();
                    break;
                case 3:
                    ShowPinnedWindows();
                    break;
                case 4:
                    // 菜单尚在 modal 循环中，延迟到消息循环返回后再退出，避免销毁窗口导致残留进程。
                    Dispatcher.UIThread.Post(Shutdown);
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    public void Shutdown()
    {
        if (_exiting)
            return;

        _exiting = true;
        Logger.Info("AppController.Shutdown 被调用");

        try
        {
            if (_foregroundHook != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(_foregroundHook);
        }
        catch (Exception ex)
        {
            Logger.Error("取消前台窗口钩子异常", ex);
        }

        _foregroundHook = IntPtr.Zero;

        // 每一步独立保护：任何一步抛异常都不能阻止进程真正退出（否则会留下无托盘的僵尸进程）。
        try
        {
            _tray?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("释放托盘图标异常", ex);
        }

        _tray = null;

        try
        {
            _pinManager.Stop();
        }
        catch (Exception ex)
        {
            Logger.Error("取消置顶异常", ex);
        }

        try
        {
            _hotkeys?.Unregister();
        }
        catch (Exception ex)
        {
            Logger.Error("注销热键异常", ex);
        }

        try
        {
            _pinnedWindowsWindow?.Close();
        }
        catch (Exception ex)
        {
            Logger.Error("关闭已置顶窗口预览异常", ex);
        }

        _pinnedWindowsWindow = null;

        try
        {
            _settingsWindow?.Close();
        }
        catch (Exception ex)
        {
            Logger.Error("关闭设置窗口异常", ex);
        }

        try
        {
            _messageWindow?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("销毁消息窗口异常", ex);
        }

        _messageWindow = null;

        try
        {
            _lifetime.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("Avalonia Shutdown 异常", ex);
        }

        // 兜底：确保进程退出，不留下僵尸进程。
        Environment.Exit(0);
    }
}
