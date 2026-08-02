using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace LuckyDogRise;

public partial class GlobalInputTracker : Node
{
    [Signal] public delegate void TypingInputOccurredEventHandler(int count);
    [Signal] public delegate void GlobalMousePressedEventHandler(Vector2I screenPosition);
    [Signal] public delegate void GlobalWinKeyPressedEventHandler();
    [Signal] public delegate void GlobalEscapeKeyPressedEventHandler();

    public GameData GameData { get; set; } = null!;

    private IntPtr _kbHook = IntPtr.Zero;
    private RawInputMouseListener _rawMouseListener = null!;
    private int _pendingPresses;
    private double _inputRewardTokens = InputRewardBucketCapacity;

    private readonly bool[] _keysDown = new bool[256];

    private LowLevelKeyboardProc _kbCallback;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F2 = 0x71;
    private const int VK_F3 = 0x72;
    private const int VK_BROWSER_BACK = 0xA6;
    private const int VK_BROWSER_HOME = 0xAC;
    private const int VK_VOLUME_MUTE = 0xAD;
    private const int VK_VOLUME_DOWN = 0xAE;
    private const int VK_VOLUME_UP = 0xAF;
    private const int VK_MEDIA_NEXT_TRACK = 0xB0;
    private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int VK_LAUNCH_MAIL = 0xB4;
    private const int VK_LAUNCH_APP2 = 0xB7;
    private const double InputRewardTokensPerSecond = 20.0;
    private const double InputRewardBucketCapacity = 40.0;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        if (@event is InputEventKey key && key.Pressed && !key.Echo &&
            !IsDebugPresentationHotkey(key.Keycode) && !IsHardwareFunctionKey(key.Keycode))
            Interlocked.Increment(ref _pendingPresses);
        // Mouse presses come from RawInputMouseListener both in and out of focus.
        // Do not count mouse events here, otherwise focused clicks are duplicated.
    }

    public override void _Ready()
    {
        _kbCallback = KbHookProc;
        var mod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbCallback, mod, 0);
        if (_kbHook == IntPtr.Zero)
            GD.PrintErr($"[GlobalInputTracker] KB hook failed (error={Marshal.GetLastWin32Error()})");
        else
            GD.Print("[GlobalInputTracker] KB hook installed");

        _rawMouseListener = new RawInputMouseListener
        {
            Name = "RawInputMouseListener",
        };
        _rawMouseListener.RawMousePressed += OnRawMousePressed;
        AddChild(_rawMouseListener);
    }

    public void SetGlobalMouseListeningEnabled(bool enabled)
    {
        if (_rawMouseListener == null)
            return;

        if (enabled)
            _rawMouseListener.StartListening();
        else
            _rawMouseListener.StopListening();
    }

    public override void _ExitTree()
    {
        if (_kbHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
        }
        if (_rawMouseListener != null)
        {
            _rawMouseListener.RawMousePressed -= OnRawMousePressed;
            _rawMouseListener.StopListening();
        }
        GD.Print("[GlobalInputTracker] Keyboard hook removed; Raw Input mouse listener stopped");
    }

    public override void _Process(double delta)
    {
        _inputRewardTokens = Math.Min(
            InputRewardBucketCapacity,
            _inputRewardTokens + Math.Max(0.0, delta) * InputRewardTokensPerSecond);

        var count = Interlocked.Exchange(ref _pendingPresses, 0);
        if (count <= 0 || GameData == null)
            return;

        var rewardedCount = Math.Min(count, (int)_inputRewardTokens);
        if (rewardedCount <= 0)
            return;

        _inputRewardTokens -= rewardedCount;
        GameData.ModifyChips(rewardedCount);
        GameData.RecordTypingInput(rewardedCount);
        EmitSignal(SignalName.TypingInputOccurred, rewardedCount);
    }

    private IntPtr KbHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (!_keysDown[vkCode])
            {
                _keysDown[vkCode] = true;
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    EmitSignal(SignalName.GlobalWinKeyPressed);
                else if (vkCode == VK_ESCAPE)
                    EmitSignal(SignalName.GlobalEscapeKeyPressed);
                if (!IsDebugPresentationHotkey(vkCode) && !IsHardwareFunctionKey(vkCode))
                    Interlocked.Increment(ref _pendingPresses);
            }
        }
        else if (nCode >= 0 && (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            _keysDown[vkCode] = false;
        }

        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private void OnRawMousePressed(int button, Vector2I screenPosition)
    {
        Interlocked.Increment(ref _pendingPresses);
        EmitSignal(SignalName.GlobalMousePressed, screenPosition);
    }

    private static bool IsDebugPresentationHotkey(Key key)
    {
#if DEBUG
        return key is Key.F2 or Key.F3;
#else
        return false;
#endif
    }

    private static bool IsDebugPresentationHotkey(int virtualKeyCode)
    {
#if DEBUG
        return virtualKeyCode is VK_F2 or VK_F3;
#else
        return false;
#endif
    }

    private static bool IsHardwareFunctionKey(Key key)
    {
        return key is Key.Volumemute or Key.Volumedown or Key.Volumeup
            or Key.Medianext or Key.Mediaprevious or Key.Mediastop
            or Key.Mediaplay or Key.Mediarecord
            or Key.Launchmail or Key.Launchmedia
            or >= Key.Launch0 and <= Key.Launchf;
    }

    private static bool IsHardwareFunctionKey(int virtualKeyCode)
    {
        // Windows groups browser, volume, media, and application-launch keys into
        // two contiguous virtual-key ranges. These controls are not typing input;
        // some keyboard wheels emit many press/release pairs for a single gesture.
        return virtualKeyCode is >= VK_BROWSER_BACK and <= VK_BROWSER_HOME
            or >= VK_VOLUME_MUTE and <= VK_VOLUME_UP
            or >= VK_MEDIA_NEXT_TRACK and <= VK_MEDIA_PLAY_PAUSE
            or >= VK_LAUNCH_MAIL and <= VK_LAUNCH_APP2;
    }
}
