using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Receives background mouse button presses through a private Win32 Raw Input
/// message window. Raw movement packets stay on the listener thread and are
/// never forwarded to Godot.
/// </summary>
public partial class RawInputMouseListener : Node
{
    [Signal] public delegate void RawMousePressedEventHandler(int button, Vector2I screenPosition);

    [Export] public bool AutoStart { get; set; } = true;
    [Export(PropertyHint.Range, "0,2,0.05")] public double AutoStartDelaySeconds { get; set; } = 0.75;

    public bool IsListening => Volatile.Read(ref _isListening) != 0;
    public bool IsAutoStartPending => _autoStartPending;
    public long TotalRawPackets => Interlocked.Read(ref _totalRawPackets);
    public long IgnoredMovementPackets => Interlocked.Read(ref _ignoredMovementPackets);

    public string LastError
    {
        get
        {
            lock (_stateLock)
                return _lastError;
        }
    }

    private readonly record struct PendingPress(MouseButton Button, Vector2I ScreenPosition);
    private readonly record struct PendingLog(bool IsError, string Message);

    private readonly ConcurrentQueue<PendingPress> _pendingPresses = new();
    private readonly ConcurrentQueue<PendingLog> _pendingLogs = new();
    private readonly object _stateLock = new();
    private Thread _messageThread;
    private ManualResetEventSlim _startupCompleted;
    private WindowProc _windowProc;
    private IntPtr _messageWindow;
    private IntPtr _rawInputBuffer;
    private uint _rawInputBufferSize;
    private uint _messageThreadId;
    private Window _hostWindow = null!;
    private string _windowClassName = string.Empty;
    private string _lastError = string.Empty;
    private int _startupSucceeded;
    private int _isListening;
    private int _rawInputRegistered;
    private long _totalRawPackets;
    private long _ignoredMovementPackets;
    private bool _autoStartPending;
    private double _autoStartDelayRemaining;
    private bool _windowGeometryRegistrationPending;

    private const int StartupTimeoutMilliseconds = 3000;
    private const int ShutdownTimeoutMilliseconds = 3000;
    // Node::NOTIFICATION_WM_WINDOW_FOCUS_IN. Godot 4.6 binds the native
    // notification, but its generated C# constant is not publicly exposed.
    private const int NotificationWindowFocusIn = 1004;
    private const int NotificationWindowSizeChanged = 1008;
    private const int NotificationWindowPositionChanged = 1012;
    private const int VirtualKeyLeftButton = 0x01;
    private const uint WmInput = 0x00FF;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmQuit = 0x0012;
    private const uint WmReregisterRawInput = 0x8001;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeMouse = 0;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const ushort HidUsagePageGeneric = 0x01;
    private const ushort HidUsageGenericMouse = 0x02;
    private const ushort RiMouseLeftButtonDown = 0x0001;
    private const ushort RiMouseRightButtonDown = 0x0004;
    private const ushort RiMouseMiddleButtonDown = 0x0010;
    private const ushort RiMouseButton4Down = 0x0040;
    private const ushort RiMouseButton5Down = 0x0100;
    private static readonly IntPtr HwndMessage = new(-3);

    public override void _Ready()
    {
        _hostWindow = GetWindow();
        _hostWindow.SizeChanged += OnHostWindowSizeChanged;

        if (AutoStart)
        {
            // Godot registers its own mouse Raw Input target during window/input
            // startup. Register after that initialization so our background target
            // remains the process's final mouse registration.
            _autoStartPending = true;
            _autoStartDelayRemaining = Math.Max(0.0, AutoStartDelaySeconds);
        }
    }

    public override void _Process(double delta)
    {
        if (_autoStartPending)
        {
            _autoStartDelayRemaining -= Math.Max(0.0, delta);
            if (_autoStartDelayRemaining <= 0.0)
            {
                _autoStartPending = false;
                StartListening();
            }
        }

        // Godot calls _set_mouse_mode_impl() from WM_EXITSIZEMOVE after a
        // non-client move/resize finishes. Wait until the drag button is up so
        // our registration runs after that final Godot registration.
        if (_windowGeometryRegistrationPending &&
            IsListening &&
            (GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) == 0)
        {
            _windowGeometryRegistrationPending = false;
            RequestRawInputReregistration();
        }

        while (_pendingLogs.TryDequeue(out var log))
        {
            if (log.IsError)
                GD.PrintErr(log.Message);
            else
                GD.Print(log.Message);
        }

        while (_pendingPresses.TryDequeue(out var press))
            EmitSignal(SignalName.RawMousePressed, (int)press.Button, press.ScreenPosition);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWindowFocusIn && IsListening)
            RequestRawInputReregistration();
        else if (what == NotificationWindowSizeChanged || what == NotificationWindowPositionChanged)
            _windowGeometryRegistrationPending = true;
    }

    public override void _ExitTree()
    {
        if (_hostWindow != null)
            _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
        StopListening();
    }

    private void OnHostWindowSizeChanged()
    {
        _windowGeometryRegistrationPending = true;
    }

    public bool StartListening()
    {
        _autoStartPending = false;
        if (!OperatingSystem.IsWindows())
        {
            SetLastError("Raw Input mouse listening is only available on Windows.");
            return false;
        }

        lock (_stateLock)
        {
            if (_messageThread is { IsAlive: true })
                return IsListening;

            _lastError = string.Empty;
            _startupCompleted?.Dispose();
            _startupCompleted = new ManualResetEventSlim(false);
            _startupSucceeded = 0;
            _messageThreadId = 0;
            _messageWindow = IntPtr.Zero;
            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "LuckyDogRise.RawInputMouse",
            };
            _messageThread.Start();
        }

        if (!_startupCompleted.Wait(StartupTimeoutMilliseconds))
        {
            SetLastError("Timed out while starting the Raw Input mouse message window.");
            StopListening();
            return false;
        }

        if (Volatile.Read(ref _startupSucceeded) != 0)
            return true;

        StopListening();
        return false;
    }

    public void StopListening()
    {
        _autoStartPending = false;
        Thread thread;
        IntPtr window;
        uint threadId;
        lock (_stateLock)
        {
            thread = _messageThread;
            window = _messageWindow;
            threadId = _messageThreadId;
        }

        if (thread == null)
            return;

        if (window != IntPtr.Zero)
            PostMessageW(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
        else if (threadId != 0)
            PostThreadMessageW(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);

        if (Thread.CurrentThread != thread && !thread.Join(ShutdownTimeoutMilliseconds))
        {
            SetLastError("Timed out while stopping the Raw Input mouse message thread.");
            return;
        }

        lock (_stateLock)
        {
            if (_messageThread == thread)
                _messageThread = null;
        }
    }

    public void ResetDiagnostics()
    {
        Interlocked.Exchange(ref _totalRawPackets, 0);
        Interlocked.Exchange(ref _ignoredMovementPackets, 0);
        while (_pendingPresses.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Godot registers its own mouse Raw Input target whenever its window regains
    /// focus. Queue our registration back onto the private message-window thread
    /// after Godot has finished processing that focus event.
    /// </summary>
    public bool RequestRawInputReregistration()
    {
        IntPtr window;
        lock (_stateLock)
            window = _messageWindow;

        if (window == IntPtr.Zero || !IsListening)
            return false;

        if (PostMessageW(window, WmReregisterRawInput, UIntPtr.Zero, IntPtr.Zero))
            return true;

        SetLastWin32Error("PostMessageW(WM_REREGISTER_RAW_INPUT)");
        return false;
    }

    private void MessageThreadMain()
    {
        ushort classAtom = 0;
        IntPtr module = IntPtr.Zero;
        try
        {
            _messageThreadId = GetCurrentThreadId();
            _windowProc = MessageWindowProc;
            module = GetModuleHandleW(null);
            _windowClassName = $"LuckyDogRise.RawInputMouse.{System.Environment.ProcessId}.{_messageThreadId}";

            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WindowProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                Instance = module,
                ClassName = _windowClassName,
            };

            classAtom = RegisterClassExW(ref windowClass);
            if (classAtom == 0)
            {
                FailStartup("RegisterClassExW");
                return;
            }

            _messageWindow = CreateWindowExW(
                0,
                _windowClassName,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                IntPtr.Zero,
                module,
                IntPtr.Zero);
            if (_messageWindow == IntPtr.Zero)
            {
                FailStartup("CreateWindowExW");
                return;
            }

            if (!RegisterRawMouse())
            {
                Volatile.Write(ref _startupSucceeded, 0);
                _startupCompleted.Set();
                return;
            }

            Volatile.Write(ref _isListening, 1);
            Volatile.Write(ref _startupSucceeded, 1);
            _startupCompleted.Set();
            QueueLog("[RawInputMouse] Listening for background mouse input.");

            while (true)
            {
                var result = GetMessageW(out var message, IntPtr.Zero, 0, 0);
                if (result == 0)
                    break;
                if (result == -1)
                {
                    SetLastWin32Error("GetMessageW");
                    break;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception exception)
        {
            SetLastError($"Raw Input mouse listener crashed: {exception.Message}");
            QueueLog($"[RawInputMouse] {exception}", isError: true);
        }
        finally
        {
            Volatile.Write(ref _isListening, 0);
            _startupCompleted?.Set();
            UnregisterRawMouse();

            if (_messageWindow != IntPtr.Zero)
            {
                DestroyWindow(_messageWindow);
                _messageWindow = IntPtr.Zero;
            }

            if (classAtom != 0 && module != IntPtr.Zero)
                UnregisterClassW(_windowClassName, module);

            if (_rawInputBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawInputBuffer);
                _rawInputBuffer = IntPtr.Zero;
                _rawInputBufferSize = 0;
            }

            _messageThreadId = 0;
            _windowProc = null;
            QueueLog("[RawInputMouse] Listener stopped.");
        }
    }

    private IntPtr MessageWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (message == WmInput)
        {
            ProcessRawInput(lParam);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        if (message == WmReregisterRawInput)
        {
            RegisterRawMouse();
            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            DestroyWindow(window);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ProcessRawInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size < headerSize)
            return;

        if (size > _rawInputBufferSize)
        {
            _rawInputBuffer = _rawInputBuffer == IntPtr.Zero
                ? Marshal.AllocHGlobal((int)size)
                : Marshal.ReAllocHGlobal(_rawInputBuffer, (IntPtr)size);
            _rawInputBufferSize = size;
        }

        var copied = GetRawInputData(rawInputHandle, RidInput, _rawInputBuffer, ref size, headerSize);
        if (copied == uint.MaxValue || copied != size)
            return;

        var header = Marshal.PtrToStructure<RawInputHeader>(_rawInputBuffer);
        if (header.Type != RimTypeMouse)
            return;

        var mouse = Marshal.PtrToStructure<RawMouse>(IntPtr.Add(_rawInputBuffer, (int)headerSize));
        Interlocked.Increment(ref _totalRawPackets);
        if (mouse.LastX != 0 || mouse.LastY != 0)
            Interlocked.Increment(ref _ignoredMovementPackets);

        var flags = mouse.ButtonFlags;
        if ((flags & RiMouseLeftButtonDown) != 0)
            QueuePress(MouseButton.Left);
        if ((flags & RiMouseRightButtonDown) != 0)
            QueuePress(MouseButton.Right);
        if ((flags & RiMouseMiddleButtonDown) != 0)
            QueuePress(MouseButton.Middle);
        if ((flags & RiMouseButton4Down) != 0)
            QueuePress(MouseButton.Xbutton1);
        if ((flags & RiMouseButton5Down) != 0)
            QueuePress(MouseButton.Xbutton2);
    }

    private void QueuePress(MouseButton button)
    {
        var position = GetCursorPos(out var point)
            ? new Vector2I(point.X, point.Y)
            : Vector2I.Zero;
        _pendingPresses.Enqueue(new PendingPress(button, position));
    }

    private bool RegisterRawMouse()
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = HidUsagePageGeneric,
                Usage = HidUsageGenericMouse,
                Flags = RidevInputSink,
                Target = _messageWindow,
            },
        };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            SetLastWin32Error("RegisterRawInputDevices");
            return false;
        }

        Volatile.Write(ref _rawInputRegistered, 1);
        return true;
    }

    private void UnregisterRawMouse()
    {
        if (Interlocked.Exchange(ref _rawInputRegistered, 0) == 0)
            return;

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = HidUsagePageGeneric,
                Usage = HidUsageGenericMouse,
                Flags = RidevRemove,
                Target = IntPtr.Zero,
            },
        };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
            SetLastWin32Error("RegisterRawInputDevices(RIDEV_REMOVE)");
    }

    private void FailStartup(string operation)
    {
        SetLastWin32Error(operation);
        Volatile.Write(ref _startupSucceeded, 0);
        _startupCompleted.Set();
    }

    private void SetLastWin32Error(string operation)
    {
        SetLastError($"{operation} failed (error={Marshal.GetLastWin32Error()}).");
    }

    private void SetLastError(string message)
    {
        lock (_stateLock)
            _lastError = message;
        QueueLog($"[RawInputMouse] {message}", isError: true);
    }

    private void QueueLog(string message, bool isError = false)
    {
        _pendingLogs.Enqueue(new PendingLog(isError, message));
    }

    private delegate IntPtr WindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProc;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public string MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    // RAWMOUSE contains a 4-byte union after usFlags. Win32 aligns that union
    // to offset 4, leaving two padding bytes at offset 2. Explicit offsets keep
    // usButtonFlags from being read out of the padding area.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct RawMouse
    {
        [FieldOffset(0)]
        public ushort Flags;

        [FieldOffset(4)]
        public ushort ButtonFlags;

        [FieldOffset(6)]
        public ushort ButtonData;

        [FieldOffset(8)]
        public uint RawButtons;

        [FieldOffset(12)]
        public int LastX;

        [FieldOffset(16)]
        public int LastY;

        [FieldOffset(20)]
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW([In] ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
