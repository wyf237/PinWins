namespace PinWins.Models;

/// <summary>
/// 应用配置，序列化为 JSON 保存到用户目录。
/// </summary>
public sealed class AppSettings
{
    /// <summary>是否已经完成过一次配置（用于判断是否首次启动）。</summary>
    public bool HasConfigured { get; set; }

    /// <summary>全局热键修饰键（MOD_* 组合）。</summary>
    public uint HotkeyModifiers { get; set; }

    /// <summary>全局热键虚拟键码。</summary>
    public uint HotkeyVirtualKey { get; set; }

    /// <summary>边框颜色（#AARRGGBB 或 #RRGGBB）。</summary>
    public string BorderColor { get; set; } = "#FF3B30";

    /// <summary>边框宽度（像素，1-10）。</summary>
    public int BorderWidth { get; set; } = 6;

    /// <summary>是否允许同时置顶多个窗口。</summary>
    public bool MultiWindow { get; set; }

    public HotkeyBinding ToBinding() => new(HotkeyModifiers, HotkeyVirtualKey);
}
