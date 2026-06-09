using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 全局键盘钩子，统计玩家打字/点击次数（即使游戏无焦点）。
/// 用于"打字统计功能"：每按一次键盘筹码 +1，按住 = 1 次，上限 1200/分钟。
/// </summary>
public partial class GlobalInputTracker : Node
{
    private IntPtr _hook = IntPtr.Zero;
    private int _pendingPresses;

    // 按住检测：缓存当前按下的键
    private readonly bool[] _keysDown = new bool[256];

    // Hook 回调必须保持引用，防止 GC
    private LowLevelKeyboardProc _hookCallback;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // 上限控制（1200/min ≈ 20/s）
    private const float MaxPerSecond = 20f;
    private float _accumulator;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public override void _Ready()
    {
        _hookCallback = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback,
            GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            GD.PrintErr($"[GlobalInputTracker] Hook install failed (error={Marshal.GetLastWin32Error()})");
        else
            GD.Print("[GlobalInputTracker] KB hook installed");
    }

    public override void _ExitTree()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            GD.Print("[GlobalInputTracker] KB hook removed");
        }
    }

    public override void _Process(double delta)
    {
        var count = Interlocked.Exchange(ref _pendingPresses, 0);
        if (count <= 0) return;

        // 上限检查：每秒最多 MaxPerSecond 次
        _accumulator += (float)delta * MaxPerSecond;
        int allowed = Mathf.FloorToInt(_accumulator);
        _accumulator -= allowed;
        int actual = Mathf.Min(count, allowed);

        if (actual <= 0) return;

        // 累加到游戏筹码
        var gm = GetNodeOrNull<GameManager>("/root/Main");
        if (gm != null)
            gm.AddChips(actual);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
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

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
