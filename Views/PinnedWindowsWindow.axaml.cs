using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using PinWins.Core;
using PinWins.Interop;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace PinWins.Views;

/// <summary>
/// 已置顶窗口的缩略图预览窗口：点击卡片激活对应窗口，点击 × 取消该窗口置顶。
/// 缩略图用 PrintWindow 抓取（Avalonia 控件没有 HWND，无法使用 DWM 缩略图）。
/// </summary>
public partial class PinnedWindowsWindow : Window
{
    private const int CardWidth = 240;
    private const int ThumbHeight = 150;
    private const int CaptureWidth = 480;
    private const int CaptureHeight = 300;

    private sealed class Card
    {
        public IntPtr Handle;
        public Border Root = null!;
        public Image Thumb = null!;
        public TextBlock Title = null!;
        public Border Dot = null!;
    }

    private readonly AppController _controller;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<IntPtr, Card> _cards = new();

    private readonly IBrush _normalBorder = new SolidColorBrush(Color.Parse("#E2E6EB"));
    private readonly IBrush _activeBorder = new SolidColorBrush(Color.Parse("#FF3B30"));

    /// <summary>最近一次真正被激活的外部窗口，用于给它对应的卡片加红框。</summary>
    private IntPtr _activeHandle;

    /// <summary>每个窗口最近一次有效预览。卡片重建时先用它填充，避免出现空白。</summary>
    private readonly Dictionary<IntPtr, WriteableBitmap> _thumbCache = new();

    private string? _signature;
    private bool _capturing;
    private int _generation;

    public PinnedWindowsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
            Icon = new WindowIcon(iconPath);

        Closing += OnClosing;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public async void Present()
    {
        RememberForeground();
        RefreshCards();

        // 先抓取缩略图再显示本窗口：本窗口是置顶的，一旦显示就会遮住目标窗口，
        // 此时 PrintWindow 只能拿到被遮挡后的陈旧/空白画面。
        await CaptureThumbnailsAsync();

        Show();
        Activate();
        ApplyHighlight();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
            return;

        RememberForeground();
        RefreshCards();
        ApplyHighlight();
        _ = CaptureThumbnailsAsync();
    }

    /// <summary>缩略图窗口关闭只是隐藏，方便下次从托盘再次打开。</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_controller.IsExiting)
            return;

        e.Cancel = true;
        Hide();
    }

    // ---- 卡片 -------------------------------------------------------------

    private void RefreshCards()
    {
        var items = _controller.GetPinnedWindows();
        var signature = string.Join(
            "|",
            items.Select(i => $"{(long)i.Handle:X}:{i.ColorHex}:{i.Title}"));

        if (signature == _signature)
            return;

        _signature = signature;
        _generation++;
        _cards.Clear();
        CardsPanel.Children.Clear();
        EmptyText.IsVisible = items.Count == 0;

        // 清掉已经不再置顶的窗口缓存。
        var handles = items.Select(i => i.Handle).ToHashSet();
        foreach (var stale in _thumbCache.Keys.Where(k => !handles.Contains(k)).ToList())
            _thumbCache.Remove(stale);

        foreach (var info in items)
            CardsPanel.Children.Add(BuildCard(info));

        ApplyHighlight();
    }

    private Control BuildCard(PinnedWindowInfo info)
    {
        var handle = info.Handle;
        var color = ColorUtil.Parse(info.ColorHex, Colors.Red);

        var thumb = new Image
        {
            Width = CardWidth,
            Height = ThumbHeight,
            Stretch = Stretch.Uniform,
        };

        if (_thumbCache.TryGetValue(handle, out var cachedThumb))
            thumb.Source = cachedThumb;

        // × 用两条对称的线绘制：字体里的 ✕ 字形本身左右/上下并不均匀，放大后看起来是歪的。
        var closeGlyph = new PathShape
        {
            Data = Geometry.Parse("M1,1 L11,11 M11,1 L1,11"),
            Stroke = Brushes.White,
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 使用 Semi 的 Solid 按钮主题 + Danger 配色：常态为实心红色，悬停/按下都是更深的红，
        // 白色的 × 始终有足够对比度，不会再出现悬停后看不清叉号的情况。
        var close = new Button
        {
            Content = closeGlyph,
            Theme = this.TryFindResource("SolidButton", out var solidTheme) ? solidTheme as ControlTheme : null,
            Classes = { "Danger" },
            Width = 26,
            Height = 26,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.Parse("#F93920")),
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0),
        };

        close.Click += (_, _) =>
        {
            _controller.UnpinWindow(handle);
            RefreshCards();
            _ = CaptureThumbnailsAsync();
        };

        var imageHost = new Grid
        {
            Background = new SolidColorBrush(Color.Parse("#EDF0F4")),
            ClipToBounds = true,
            Children = { thumb, close },
        };

        var dot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(info.Title) ? "未命名窗口" : info.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var footer = new Grid
        {
            Margin = new Thickness(10, 8, 10, 10),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        };
        footer.Children.Add(dot);
        Grid.SetColumn(title, 1);
        footer.Children.Add(title);

        var root = new Border
        {
            Width = CardWidth,
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = _normalBorder,
            Background = Brushes.White,
            ClipToBounds = true,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Children = { imageHost, footer } },
        };

        root.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed)
                return;

            e.Handled = true;
            _controller.ActivateWindow(handle);
            _activeHandle = handle;
            ApplyHighlight();
        };

        _cards[handle] = new Card
        {
            Handle = handle,
            Root = root,
            Thumb = thumb,
            Title = title,
            Dot = dot,
        };

        return root;
    }

    // ---- 激活窗口标记 -----------------------------------------------------

    /// <summary>
    /// 记录当前前台窗口。本程序自己的窗口（预览面板、托盘菜单、设置窗口）获得前台时，
    /// 改用控制器持续跟踪的上一个外部窗口，否则一打开面板就会丢失“正在使用哪个窗口”的标记。
    /// </summary>
    private void RememberForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero && !WindowHelper.IsOwnProcessWindow(foreground))
            _activeHandle = foreground;
        else if (_controller.ActiveWindowHandle != IntPtr.Zero)
            _activeHandle = _controller.ActiveWindowHandle;
    }

    /// <summary>给当前激活窗口对应的卡片加红色边框。</summary>
    private void ApplyHighlight()
    {
        foreach (var card in _cards.Values)
        {
            card.Root.BorderBrush = card.Handle == _activeHandle ? _activeBorder : _normalBorder;
        }
    }

    // ---- 缩略图 -----------------------------------------------------------

    private async Task CaptureThumbnailsAsync()
    {
        if (_capturing)
            return;

        // 注意：不要因为“目标被本窗口遮挡”就跳过抓取。
        // PrintWindow(PW_RENDERFULLCONTENT) 让窗口自己渲染到内存 DC，与是否被遮挡无关；
        // 之前跳过被遮挡窗口，导致面板打开后缩略图永远不再刷新、取消置顶后重建的卡片一直空白。
        var handles = _cards.Keys.ToList();
        if (handles.Count == 0)
            return;

        _capturing = true;
        var generation = _generation;

        try
        {
            var results = await Task.Run(() =>
            {
                var list = new List<(IntPtr Handle, byte[]? Pixels)>(handles.Count);
                foreach (var handle in handles)
                    list.Add((handle, WindowThumbnail.Capture(handle, CaptureWidth, CaptureHeight)));

                return list;
            });

            if (generation != _generation)
                return;

            foreach (var (handle, pixels) in results)
            {
                if (pixels == null || !_cards.TryGetValue(handle, out var card))
                    continue;

                // 个别窗口在后台/被遮挡时会返回纯色画面，此时保留上一次的有效预览，避免卡片变空白。
                if (IsBlank(pixels))
                    continue;

                var bitmap = CreateBitmap(pixels, CaptureWidth, CaptureHeight);
                if (bitmap == null)
                    continue;

                _thumbCache[handle] = bitmap;
                card.Thumb.Source = bitmap;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("生成窗口缩略图异常", ex);
        }
        finally
        {
            _capturing = false;
        }
    }

    /// <summary>整张图几乎只有一种颜色，说明这次抓取拿到了空画面。</summary>
    private static bool IsBlank(byte[] pixels)
    {
        if (pixels.Length < 8)
            return true;

        var b = pixels[0];
        var g = pixels[1];
        var r = pixels[2];

        for (var i = 4 * 37; i < pixels.Length; i += 4 * 37)
        {
            if (Math.Abs(pixels[i] - b) > 8 ||
                Math.Abs(pixels[i + 1] - g) > 8 ||
                Math.Abs(pixels[i + 2] - r) > 8)
                return false;
        }

        return true;
    }

    private static WriteableBitmap? CreateBitmap(byte[] pixels, int width, int height)
    {
        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);

            using var buffer = bitmap.Lock();

            var rowBytes = width * 4;
            if (buffer.RowBytes == rowBytes)
            {
                Marshal.Copy(pixels, 0, buffer.Address, rowBytes * height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Logger.Error("创建缩略图位图异常", ex);
            return null;
        }
    }
}
