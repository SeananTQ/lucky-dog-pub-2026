using Godot;
using System;
using System.Threading;

namespace LuckyDogRise;

public partial class SingleInstanceGuard : Node
{
    private const int ActivationAcknowledgementTimeoutMilliseconds = 1500;
    private const int ShutdownHandoffTimeoutMilliseconds = 1500;

    private static SingleInstanceGuard _instance;

    private System.Threading.Mutex _instanceMutex;
    private EventWaitHandle _activationRequest;
    private EventWaitHandle _activationAcknowledgement;
    private bool _ownsMutex;

    public event Action ActivationRequested;

    public override void _EnterTree()
    {
        _instance = this;
        if (!OperatingSystem.IsWindows())
            return;

        var channel = BuildInfo.Channel.ToString();
        var mutexName = $@"Local\LuckyDogRise.{channel}.Instance";
        var requestName = $@"Local\LuckyDogRise.{channel}.Activate";
        var acknowledgementName = $@"Local\LuckyDogRise.{channel}.Activated";

        _instanceMutex = new System.Threading.Mutex(false, mutexName);
        _activationRequest = new EventWaitHandle(false, EventResetMode.AutoReset, requestName);
        _activationAcknowledgement = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            acknowledgementName);

        _ownsMutex = TryAcquireMutex(0);
        if (_ownsMutex)
            return;

        _activationAcknowledgement.Reset();
        _activationRequest.Set();
        if (_activationAcknowledgement.WaitOne(ActivationAcknowledgementTimeoutMilliseconds))
        {
            GD.Print("[SingleInstance] Existing game instance was activated; closing duplicate launch.");
            GetTree().Quit();
            return;
        }

        // The previous process may be in its shutdown handoff and unable to acknowledge.
        _ownsMutex = TryAcquireMutex(ShutdownHandoffTimeoutMilliseconds);
        if (_ownsMutex)
        {
            GD.Print("[SingleInstance] Previous instance exited during handoff; continuing startup.");
            return;
        }

        OS.Alert(
            "Lucky Dog Rise is already running, but its window did not respond.\n\n" +
            "Please stop the game from Steam and launch it again. If Steam cannot stop it, " +
            "end LuckyDogRise from Windows Task Manager.",
            "Lucky Dog Rise");
        GetTree().Quit(3);
    }

    public override void _Process(double delta)
    {
        if (!_ownsMutex || _activationRequest == null || !_activationRequest.WaitOne(0))
            return;

        try
        {
            ActivationRequested?.Invoke();
        }
        finally
        {
            _activationAcknowledgement?.Set();
        }
    }

    public override void _ExitTree()
    {
        ReleaseOwnership();
        _activationRequest?.Dispose();
        _activationAcknowledgement?.Dispose();
        _instanceMutex?.Dispose();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    public static void ReleaseForRestart()
    {
        _instance?.ReleaseOwnership();
    }

    public static bool ReacquireAfterFailedRestart()
    {
        if (_instance == null || !OperatingSystem.IsWindows())
            return true;

        _instance._ownsMutex = _instance.TryAcquireMutex(0);
        return _instance._ownsMutex;
    }

    private bool TryAcquireMutex(int timeoutMilliseconds)
    {
        try
        {
            return _instanceMutex.WaitOne(timeoutMilliseconds);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private void ReleaseOwnership()
    {
        if (!_ownsMutex || _instanceMutex == null)
            return;

        _instanceMutex.ReleaseMutex();
        _ownsMutex = false;
    }
}
