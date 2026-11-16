using System;
using System.Text;
using PinWins.Interop;

namespace PinWins.Models;

/// <summary>
/// 一个全局快捷键定义（修饰键 + 虚拟键码）。
/// </summary>
public sealed class HotkeyBinding
{
    public uint Modifiers { get; set; }
    public uint VirtualKey { get; set; }

    public bool IsValid => Modifiers != 0 && VirtualKey != 0;

    public HotkeyBinding() { }

    public HotkeyBinding(uint modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public override string ToString() => Format(Modifiers, VirtualKey);

    public static string Format(uint modifiers, uint virtualKey)
    {
        if (modifiers == 0 && virtualKey == 0)
            return "未设置";

        var sb = new StringBuilder();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) sb.Append("Ctrl + ");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) sb.Append("Alt + ");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) sb.Append("Shift + ");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) sb.Append("Win + ");
        sb.Append(GetKeyName(virtualKey));
        return sb.ToString();
    }

    public static string GetKeyName(uint vk)
    {
        // 字母
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        // 主键盘数字
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        // 小键盘数字
        if (vk is >= 0x60 and <= 0x69)
            return "Num" + (vk - 0x60);
        // 功能键 F1..F24
        if (vk is >= 0x70 and <= 0x87)
            return "F" + (vk - 0x6F);

        return vk switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "0x" + vk.ToString("X2"),
        };
    }
}
