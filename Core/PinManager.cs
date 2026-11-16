using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using PinWins.Interop;

namespace PinWins.Core;

/// <summary>供缩略图窗口展示的已置顶窗口信息。</summary>
public sealed record PinnedWindowInfo(IntPtr Handle, string Title, string ColorHex);

/// <summary>
/// 负责窗口置顶/取消置顶，并为每个已置顶窗口维护一圈覆盖描边（<see cref="BorderOverlay"/>）。
/// 同时把 Windows 11 的 DWM 原生边框色作为附加着色，让标准边框窗口内外都有提示。
/// </summary>
internal sealed class PinManager
{
    private sealed class Pin
    {
        public IntPtr Target;
        public int OriginalBorderColor;
        public bool HasOriginalBorderColor;
        public uint AppliedColor;
        public string ColorHex = "#FF3B30";
        public BorderOverlay? Overlay;
    }

    // 多窗口置顶时用于区分的调色板。
    private static readonly string[] Palette =
    {
        "#F44336", "#E91E63", "#9C27B0", "#3F51B5", "#2196F3",
        "#00BCD4", "#009688", "#4CAF50", "#FF9800", "#795548",
    };

    private readonly List<Pin> _pins = new();
    private readonly List<string> _paletteCache = new();

    private DispatcherTimer? _syncTimer;
    private bool _multi;
    private string _baseColorHex = "#FF3B30";
    private int _borderWidth = 6;
    private bool _paletteDirty = true;
    private bool _warnedUnsupported;

    public void Start()
    {
        // WinEvent 偶尔会漏发位置事件（WPF / Electron 窗口尤其明显），
        // 这里用低频定时器兜底校对，保证描边始终贴合窗体。
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _syncTimer.Tick += (_, _) => SyncOverlays();
        _syncTimer.Start();
    }

    public void Stop()
    {
        _syncTimer?.Stop();
        _syncTimer = null;
        ClearAll();
    }

    private void SyncOverlays()
    {
        if (_pins.Count == 0)
            return;

        foreach (var pin in _pins.ToArray())
        {
            try
            {
                pin.Overlay?.Reposition();
            }
            catch (Exception ex)
            {
                Logger.Error("校对边框位置异常", ex);
            }
        }
    }

    /// <summary>应用配置（是否多窗口、基础颜色、边框宽度）。</summary>
    public void Configure(bool multiWindow, string baseColorHex, int borderWidth)
    {
        try
        {
            _multi = multiWindow;
            _baseColorHex = string.IsNullOrWhiteSpace(baseColorHex) ? "#FF3B30" : baseColorHex;
            _borderWidth = BorderOverlay.ClampWidth(borderWidth);
            _paletteDirty = true;

            PruneDeadWindows();

            if (!multiWindow && _pins.Count > 1)
            {
                for (var i = _pins.Count - 1; i >= 1; i--)
                    RemoveAt(i);
            }

            ApplyColors();
        }
        catch (Exception ex)
        {
            Logger.Error("PinManager.Configure 异常", ex);
        }
    }

    /// <summary>
    /// 热键行为：
    /// 指向已置顶窗口 -> 取消该窗口置顶；
    /// 指向未置顶窗口 -> （非多窗口模式下先取消其它置顶窗口）再置顶该窗口。
    /// </summary>
    public void Toggle(IntPtr target)
    {
        try
        {
            if (target == IntPtr.Zero || !NativeMethods.IsWindow(target))
                return;

            // 不要把本程序自己的窗口（设置、缩略图面板等）也置顶，否则会出现自己套自己的描边。
            if (WindowHelper.IsOwnProcessWindow(target))
            {
                Logger.Info("忽略对本程序窗口的置顶请求");
                return;
            }

            PruneDeadWindows();

            var existing = _pins.FirstOrDefault(p => p.Target == target);
            if (existing != null)
            {
                Logger.Info($"取消置顶窗口 0x{target.ToInt64():X}");
                Remove(existing);
                return;
            }

            if (!_multi)
                ClearAll();

            Logger.Info($"置顶窗口 0x{target.ToInt64():X}");
            PinWindow(target);
        }
        catch (Exception ex)
        {
            Logger.Error("PinManager.Toggle 异常", ex);
        }
    }

    /// <summary>取消指定窗口的置顶（缩略图窗口使用）。</summary>
    public void Unpin(IntPtr target)
    {
        try
        {
            var pin = _pins.FirstOrDefault(p => p.Target == target);
            if (pin != null)
            {
                Logger.Info($"取消置顶窗口 0x{target.ToInt64():X}");
                Remove(pin);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("PinManager.Unpin 异常", ex);
        }
    }

    /// <summary>当前已置顶窗口快照（会顺带清理已关闭的窗口）。</summary>
    public IReadOnlyList<PinnedWindowInfo> GetPinnedWindows()
    {
        try
        {
            PruneDeadWindows();

            return _pins
                .Select(p => new PinnedWindowInfo(p.Target, WindowHelper.GetTitle(p.Target), p.ColorHex))
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.Error("PinManager.GetPinnedWindows 异常", ex);
            return Array.Empty<PinnedWindowInfo>();
        }
    }

    public void ClearAll()
    {
        for (var i = _pins.Count - 1; i >= 0; i--)
            RemoveAt(i);
    }

    private void PinWindow(IntPtr target)
    {
        var originalColor = NativeMethods.DWMWA_COLOR_DEFAULT;
        var hasOriginalColor = NativeMethods.DwmGetWindowAttribute(
            target, NativeMethods.DWMWA_BORDER_COLOR, out var current, sizeof(int)) == 0;
        if (hasOriginalColor)
            originalColor = current;

        _pins.Add(new Pin
        {
            Target = target,
            OriginalBorderColor = originalColor,
            HasOriginalBorderColor = hasOriginalColor,
        });

        // 先置顶，再创建描边，避免描边窗口被目标窗口压住。
        WindowHelper.SetTopmost(target, true);
        ApplyColors();
    }

    private void Remove(Pin pin)
    {
        var index = _pins.IndexOf(pin);
        if (index >= 0)
            RemoveAt(index);
    }

    private void RemoveAt(int index)
    {
        var pin = _pins[index];
        _pins.RemoveAt(index);

        DisposeOverlay(pin);

        if (NativeMethods.IsWindow(pin.Target))
            RestoreWindow(pin);

        // 其余窗口重新分配颜色，避免不同窗口出现重复颜色。
        ApplyColors();
    }

    private static void DisposeOverlay(Pin pin)
    {
        try
        {
            pin.Overlay?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("释放边框覆盖窗口异常", ex);
        }

        pin.Overlay = null;
    }

    private static void RestoreWindow(Pin pin)
    {
        var restore = pin.HasOriginalBorderColor
            ? pin.OriginalBorderColor
            : NativeMethods.DWMWA_COLOR_DEFAULT;

        NativeMethods.DwmSetWindowAttribute(
            pin.Target, NativeMethods.DWMWA_BORDER_COLOR, ref restore, sizeof(int));

        // 取消置顶时一律撤销置顶状态：无论窗口之前是否已经被其它程序/上次运行置顶，
        // 只要用户点了“取消置顶”，就应该真正落回普通层级。
        WindowHelper.SetTopmost(pin.Target, false);
    }

    /// <summary>清理已关闭窗口的记录（窗口已不存在，无需恢复任何状态）。</summary>
    private void PruneDeadWindows()
    {
        var removed = false;

        for (var i = _pins.Count - 1; i >= 0; i--)
        {
            if (NativeMethods.IsWindow(_pins[i].Target))
                continue;

            DisposeOverlay(_pins[i]);
            _pins.RemoveAt(i);
            removed = true;
        }

        if (removed)
            ApplyColors();
    }

    /// <summary>为每个置顶窗口分配颜色，并同步 DWM 边框色与覆盖描边。</summary>
    private void ApplyColors()
    {
        for (var i = 0; i < _pins.Count; i++)
        {
            var pin = _pins[i];
            if (!NativeMethods.IsWindow(pin.Target))
                continue;

            var hex = ColorHexFor(i);
            var colorRef = ColorUtil.ToColorRef(ColorUtil.Parse(hex, Colors.Red));

            pin.ColorHex = hex;

            ApplyDwmBorder(pin, colorRef);

            if (pin.Overlay == null)
            {
                try
                {
                    pin.Overlay = new BorderOverlay(pin.Target, colorRef, _borderWidth);
                }
                catch (Exception ex)
                {
                    Logger.Error($"创建边框覆盖窗口失败 0x{pin.Target.ToInt64():X}", ex);
                }
            }
            else
            {
                pin.Overlay.Update(colorRef, _borderWidth);
            }
        }
    }

    private void ApplyDwmBorder(Pin pin, uint colorRef)
    {
        if (colorRef == pin.AppliedColor)
            return;

        var value = unchecked((int)colorRef);
        var hr = NativeMethods.DwmSetWindowAttribute(
            pin.Target, NativeMethods.DWMWA_BORDER_COLOR, ref value, sizeof(int));

        if (hr == 0)
        {
            pin.AppliedColor = colorRef;
        }
        else if (!_warnedUnsupported)
        {
            // 多见于 Windows 10（不支持该属性），此时仍由覆盖描边提供可见边框。
            _warnedUnsupported = true;
            Logger.Error($"DwmSetWindowAttribute(DWMWA_BORDER_COLOR) 失败, hr=0x{hr:X8}，当前系统可能不支持彩色边框。");
        }
    }

    private string ColorHexFor(int index)
    {
        if (_paletteDirty)
            RebuildPaletteCache();

        return _paletteCache[index % _paletteCache.Count];
    }

    private void RebuildPaletteCache()
    {
        _paletteCache.Clear();
        _paletteCache.Add(_baseColorHex);

        // 跳过与基础色过于接近的颜色，否则多个窗口看起来像是同一种颜色。
        var baseColor = ColorUtil.Parse(_baseColorHex, Colors.Red);
        foreach (var hex in Palette)
        {
            var color = ColorUtil.Parse(hex, Colors.Red);
            if (!IsDistinctColor(baseColor, color))
                continue;

            if (!_paletteCache.Any(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase)))
                _paletteCache.Add(hex);
        }

        _paletteDirty = false;
    }

    /// <summary>判断两颜色是否有足够视觉差异（RGB 欧氏距离平方 &gt; 3000）。</summary>
    private static bool IsDistinctColor(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return dr * dr + dg * dg + db * db > 3000;
    }
}
