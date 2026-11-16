using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PinWins.Core;
using PinWins.Interop;
using PinWins.Models;

namespace PinWins.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _captureTimer;

    private HotkeyBinding _binding = new();
    private bool _capturing;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
            Icon = new WindowIcon(iconPath);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        HotkeyBox.PointerPressed += (_, _) => BeginCapture();
        Closing += OnClosing;

        BorderColorPicker.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorPicker.ColorProperty)
                UpdateColorText();
        };

        BorderWidthSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                UpdateBorderWidthText();
        };

        // 快捷键捕获超时：10 秒未输入则自动取消，避免用户卡住。
        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _captureTimer.Tick += (_, _) =>
        {
            if (_capturing)
            {
                EndCapture();
                UpdateHotkeyText();
            }
        };
    }

    public void Present(string? error)
    {
        LoadFromSettings();

        if (!string.IsNullOrEmpty(error))
            ShowError(error);
        else
            HideError();

        Show();
        Activate();
    }

    private void LoadFromSettings()
    {
        var settings = _controller.Settings;

        _binding = settings.ToBinding();
        UpdateHotkeyText();

        BorderColorPicker.Color = ColorUtil.Parse(settings.BorderColor, Colors.Red);
        UpdateColorText();

        BorderWidthSlider.Value = BorderOverlay.ClampWidth(settings.BorderWidth);
        UpdateBorderWidthText();

        MultiWindowCheck.IsChecked = settings.MultiWindow;
    }

    // ---- 快捷键捕获 -------------------------------------------------------

    private void BeginCapture()
    {
        _capturing = true;
        HotkeyText.Text = "请按下快捷键…";
        HotkeyBox.BorderBrush = new SolidColorBrush(Color.Parse("#0064FA"));
        HotkeyHint.Text = "按下包含修饰键的组合键（Esc 取消）";
        _captureTimer.Start();
        Focus();
    }

    private void EndCapture()
    {
        _capturing = false;
        _captureTimer.Stop();
        HotkeyBox.BorderBrush = new SolidColorBrush(Color.Parse("#D9D9D9"));
        HotkeyHint.Text = "点击上方区域，然后按下组合键（必须包含 Ctrl / Alt / Shift / Win 之一，Esc 取消）";
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturing)
            return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndCapture();
            UpdateHotkeyText();
            return;
        }

        if (IsModifierKey(e.Key))
            return;

        var modifiers = ToModifiers(e.KeyModifiers);
        if (modifiers == 0)
        {
            ShowError("快捷键必须包含 Ctrl / Alt / Shift / Win 中的至少一个修饰键。");
            return;
        }

        if (!TryMapKey(e.Key, out var virtualKey))
        {
            ShowError($"暂不支持该按键：{e.Key}");
            return;
        }

        _binding = new HotkeyBinding(modifiers, virtualKey);
        EndCapture();
        HideError();
        UpdateHotkeyText();
    }

    private void UpdateHotkeyText() => HotkeyText.Text = _binding.ToString();

    private static bool IsModifierKey(Key key) => key is
        Key.LeftShift or Key.RightShift or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin;

    private static uint ToModifiers(KeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(KeyModifiers.Meta)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    private static bool TryMapKey(Key key, out uint virtualKey)
    {
        virtualKey = 0;

        if (key >= Key.A && key <= Key.Z)
        {
            virtualKey = (uint)(0x41 + (key - Key.A));
            return true;
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            virtualKey = (uint)(0x30 + (key - Key.D0));
            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            virtualKey = (uint)(0x60 + (key - Key.NumPad0));
            return true;
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            virtualKey = (uint)(0x70 + (key - Key.F1));
            return true;
        }

        virtualKey = key switch
        {
            Key.Space => 0x20,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Back => 0x08,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.OemMinus => 0xBD,
            Key.OemPlus => 0xBB,
            Key.OemOpenBrackets => 0xDB,
            Key.OemCloseBrackets => 0xDD,
            Key.OemSemicolon => 0xBA,
            Key.OemQuotes => 0xDE,
            Key.OemComma => 0xBC,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemPipe => 0xDC,
            _ => 0u,
        };

        return virtualKey != 0;
    }

    // ---- 颜色 ------------------------------------------------------------

    private void UpdateColorText()
        => ColorHexText.Text = ColorUtil.ToHex(BorderColorPicker.Color);

    private void UpdateBorderWidthText()
        => BorderWidthText.Text = $"{(int)Math.Round(BorderWidthSlider.Value)} px";

    // ---- 事件 ------------------------------------------------------------

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var color = BorderColorPicker.Color;
        var hex = ColorUtil.ToHex(color);
        var multiWindow = MultiWindowCheck.IsChecked == true;
        var borderWidth = BorderOverlay.ClampWidth((int)Math.Round(BorderWidthSlider.Value));

        if (_controller.TryApplySettings(_binding, hex, multiWindow, borderWidth, out var error))
        {
            HideError();
            Hide();
        }
        else
        {
            ShowError(error ?? "保存失败。");
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        // 程序常驻托盘，关闭/取消只是隐藏窗口，随时可以从托盘菜单再次打开。
        Hide();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_controller.IsExiting)
            return;

        e.Cancel = true;
        Hide();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void HideError()
    {
        ErrorText.IsVisible = false;
        ErrorText.Text = string.Empty;
    }
}
