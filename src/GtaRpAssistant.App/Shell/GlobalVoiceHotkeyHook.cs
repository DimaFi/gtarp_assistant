using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GtaRpAssistant.App.Shell;

public enum VoiceHotkeyGesture
{
    None,
    Pressed,
    Released,
}

public sealed class VoiceHotkeyGestureTracker
{
    private const int VoiceKey = 0x41;
    private bool _pressed;

    public VoiceHotkeyGesture Update(int virtualKey, bool isKeyDown, bool isKeyUp, bool controlDown, bool altDown)
    {
        if (virtualKey != VoiceKey) return VoiceHotkeyGesture.None;
        if (isKeyDown)
        {
            if (_pressed || !controlDown || !altDown) return VoiceHotkeyGesture.None;
            _pressed = true;
            return VoiceHotkeyGesture.Pressed;
        }
        if (isKeyUp && _pressed)
        {
            _pressed = false;
            return VoiceHotkeyGesture.Released;
        }
        return VoiceHotkeyGesture.None;
    }

    public void Reset() => _pressed = false;
}

public sealed class GlobalVoiceHotkeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;

    private readonly VoiceHotkeyGestureTracker _tracker = new();
    private readonly HookProcedure _procedure;
    private nint _hook;

    public GlobalVoiceHotkeyHook() => _procedure = OnKeyboard;

    public event EventHandler<VoiceHotkeyGesture>? Gesture;

    public void Start()
    {
        if (_hook != 0) return;
        _hook = SetWindowsHookEx(WhKeyboardLl, _procedure, GetModuleHandle(null), 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить voice key-up hook.");
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var gesture = _tracker.Update(
                Marshal.ReadInt32(lParam),
                message is WmKeyDown or WmSysKeyDown,
                message is WmKeyUp or WmSysKeyUp,
                IsDown(VkControl),
                IsDown(VkMenu));
            if (gesture != VoiceHotkeyGesture.None) Gesture?.Invoke(this, gesture);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    public void Dispose()
    {
        _tracker.Reset();
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookId, HookProcedure procedure, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
