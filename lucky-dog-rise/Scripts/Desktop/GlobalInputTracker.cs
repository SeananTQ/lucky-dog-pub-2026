using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace LuckyDogRise;

public partial class GlobalInputTracker : Node
{
    public GameData GameData { get; set; } = null!;

    private IntPtr _kbHook = IntPtr.Zero;
    private IntPtr _msHook = IntPtr.Zero;
    private int _pendingPresses;

    private readonly bool[] _keysDown = new bool[256];

    private LowLevelKeyboardProc _kbCallback;
    private LowLevelMouseProc _msCallback;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
            Interlocked.Increment(ref _pendingPresses);
        // 鼠标：WH_MOUSE_LL 有/无焦点都能收到，不走 _Input 避免重复
    }

    public override void _Ready()
    {
        var mod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);

        _kbCallback = KbHookProc;
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbCallback, mod, 0);
        if (_kbHook == IntPtr.Zero)
            GD.PrintErr($"[GlobalInputTracker] KB hook failed (error={Marshal.GetLastWin32Error()})");
        else
            GD.Print("[GlobalInputTracker] KB hook installed");

        _msCallback = MsHookProc;
        _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msCallback, mod, 0);
        if (_msHook == IntPtr.Zero)
            GD.PrintErr($"[GlobalInputTracker] Mouse hook failed (error={Marshal.GetLastWin32Error()})");
        else
            GD.Print("[GlobalInputTracker] Mouse hook installed");
    }

    public override void _ExitTree()
    {
        if (_kbHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
        }
        if (_msHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_msHook);
            _msHook = IntPtr.Zero;
        }
        GD.Print("[GlobalInputTracker] Hooks removed");
    }

    public override void _Process(double delta)
    {
        var count = Interlocked.Exchange(ref _pendingPresses, 0);
        if (count > 0 && GameData != null)
            GameData.ModifyChips(count);
    }

    private IntPtr KbHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (!_keysDown[vkCode])
            {
                _keysDown[vkCode] = true;
                Interlocked.Increment(ref _pendingPresses);
            }
        }
        else if (nCode >= 0 && wParam == (IntPtr)WM_KEYUP)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            _keysDown[vkCode] = false;
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MsHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN ||
                msg == WM_MBUTTONDOWN || msg == WM_XBUTTONDOWN)
                Interlocked.Increment(ref _pendingPresses);
        }

        return CallNextHookEx(_msHook, nCode, wParam, lParam);
    }
}
