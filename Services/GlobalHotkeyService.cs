using PinWins.Core;
using PinWins.Interop;
using PinWins.Models;

namespace PinWins.Services;

/// <summary>
/// 封装 RegisterHotKey / UnregisterHotKey。
/// 只注册用户配置的那一个组合键，不占用任何其它热键。
/// </summary>
internal sealed class GlobalHotkeyService
{
    private const int HotkeyId = 0x1001;

    private readonly IntPtr _hwnd;
    private bool _registered;

    public GlobalHotkeyService(IntPtr hwnd) => _hwnd = hwnd;

    public bool IsRegistered => _registered;

    public bool Register(HotkeyBinding binding)
    {
        Unregister();

        if (binding is null || !binding.IsValid)
            return false;

        var ok = NativeMethods.RegisterHotKey(
            _hwnd,
            HotkeyId,
            binding.Modifiers | (uint)NativeMethods.MOD_NOREPEAT,
            binding.VirtualKey);

        _registered = ok;
        Logger.Info($"RegisterHotKey({binding}, id=0x{HotkeyId:X}) => {(ok ? "成功" : "失败")}");
        return ok;
    }

    public void Unregister()
    {
        if (!_registered)
            return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
        Logger.Info("UnregisterHotKey 完成");
    }
}
