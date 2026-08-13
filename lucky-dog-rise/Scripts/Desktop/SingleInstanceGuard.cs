#nullable enable

using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace LuckyDogRise;

public partial class SingleInstanceGuard : Node
{
    private const int ActivationAcknowledgementTimeoutMilliseconds = 1500;

    public enum InstanceState
    {
        Starting,
        Interactive,
        ShuttingDown,
    }

    public enum DuplicateLaunchResult
    {
        ExistingActivated,
        AccountConflict,
        IdentityUnavailable,
        ExistingUnresponsive,
    }

    private sealed class PublishedInstanceIdentity
    {
        public int ProcessId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
    }

    private static SingleInstanceGuard? _instance;

    private System.Threading.Mutex? _instanceMutex;
    private EventWaitHandle? _activationRequest;
    private EventWaitHandle? _activationAcknowledgement;
    private bool _ownsMutex;
    private string _identityStatePath = string.Empty;
    private string _publishedProvider = string.Empty;
    private string _publishedAccountId = string.Empty;

    public Func<bool>? ActivationRequested;
    public InstanceState State { get; private set; } = InstanceState.Starting;
    public bool IsPrimaryInstance => !OperatingSystem.IsWindows() || _ownsMutex;

    public override void _EnterTree()
    {
        _instance = this;
        if (!OperatingSystem.IsWindows())
            return;

        var channel = BuildInfo.Channel.ToString();
        var mutexName = $@"Local\LuckyDogRise.{channel}.Instance";
        var requestName = $@"Local\LuckyDogRise.{channel}.Activate";
        var acknowledgementName = $@"Local\LuckyDogRise.{channel}.Activated";
        _identityStatePath = ProjectSettings.GlobalizePath($"user://instance/{channel.ToLowerInvariant()}.json");

        _instanceMutex = new System.Threading.Mutex(false, mutexName);
        _activationRequest = new EventWaitHandle(false, EventResetMode.AutoReset, requestName);
        _activationAcknowledgement = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            acknowledgementName);

        _ownsMutex = TryAcquireMutex(0);
        if (_ownsMutex)
        {
            PublishIdentity(string.Empty, string.Empty);
            return;
        }
    }

    public void PublishAccountIdentity(string provider, string accountId)
    {
        _publishedProvider = provider ?? string.Empty;
        _publishedAccountId = accountId ?? string.Empty;
        if (_ownsMutex)
            PublishIdentity(_publishedProvider, _publishedAccountId);
    }

    public DuplicateLaunchResult ResolveDuplicateLaunch(
        string provider,
        string accountId,
        out string existingAccountId)
    {
        existingAccountId = string.Empty;
        if (_ownsMutex)
            throw new InvalidOperationException("The primary instance cannot resolve itself as a duplicate.");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(accountId))
            return DuplicateLaunchResult.IdentityUnavailable;

        PublishedInstanceIdentity? existing = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            existing = ReadPublishedIdentity();
            if (existing != null && existing.AccountId.Length > 0)
                break;
            Thread.Sleep(50);
        }

        if (existing == null || existing.AccountId.Length == 0)
            return DuplicateLaunchResult.ExistingUnresponsive;

        existingAccountId = existing.AccountId;
        if (!string.Equals(existing.Provider, provider, StringComparison.Ordinal)
            || !string.Equals(existing.AccountId, accountId, StringComparison.Ordinal))
            return DuplicateLaunchResult.AccountConflict;

        _activationAcknowledgement!.Reset();
        _activationRequest!.Set();
        return _activationAcknowledgement.WaitOne(ActivationAcknowledgementTimeoutMilliseconds)
            ? DuplicateLaunchResult.ExistingActivated
            : DuplicateLaunchResult.ExistingUnresponsive;
    }

    public override void _Process(double delta)
    {
        if (!_ownsMutex || _activationRequest == null || !_activationRequest.WaitOne(0))
            return;

        try
        {
            bool accepted = State != InstanceState.ShuttingDown
                && ActivationRequested?.Invoke() == true;
            if (accepted)
                _activationAcknowledgement?.Set();
            else
                GD.Print("[SingleInstance] Activation request was not acknowledged because the instance is shutting down.");
        }
        catch (Exception exception)
        {
            GD.PushError($"[SingleInstance] Activation request failed: {exception}");
        }
    }

    public override void _ExitTree()
    {
        if (_ownsMutex)
            DeletePublishedIdentity();
        ReleaseOwnership();
        _activationRequest?.Dispose();
        _activationAcknowledgement?.Dispose();
        _instanceMutex?.Dispose();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    public static void ReleaseForRestart()
    {
        if (_instance == null)
            return;

        _instance.State = InstanceState.ShuttingDown;
        _instance.ReleaseOwnership();
    }

    public static bool ReacquireAfterFailedRestart()
    {
        if (_instance == null || !OperatingSystem.IsWindows())
            return true;

        _instance._ownsMutex = _instance.TryAcquireMutex(0);
        if (_instance._ownsMutex)
        {
            _instance.State = InstanceState.Interactive;
            _instance.PublishIdentity(
                _instance._publishedProvider,
                _instance._publishedAccountId);
        }
        return _instance._ownsMutex;
    }

    public void MarkInteractive()
    {
        if (_ownsMutex && State != InstanceState.ShuttingDown)
            State = InstanceState.Interactive;
    }

    public void BeginShutdown()
    {
        State = InstanceState.ShuttingDown;
    }

    private bool TryAcquireMutex(int timeoutMilliseconds)
    {
        try
        {
            return _instanceMutex!.WaitOne(timeoutMilliseconds);
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

    private void PublishIdentity(string provider, string accountId)
    {
        try
        {
            var directory = Path.GetDirectoryName(_identityStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var tempPath = $"{_identityStatePath}.tmp.{System.Environment.ProcessId}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new PublishedInstanceIdentity
            {
                ProcessId = System.Environment.ProcessId,
                Provider = provider ?? string.Empty,
                AccountId = accountId ?? string.Empty,
            }));
            File.Move(tempPath, _identityStatePath, overwrite: true);
        }
        catch (Exception exception)
        {
            GD.PushError($"[SingleInstance] Failed to publish account identity: {exception.Message}");
        }
    }

    private PublishedInstanceIdentity? ReadPublishedIdentity()
    {
        try
        {
            return File.Exists(_identityStatePath)
                ? JsonSerializer.Deserialize<PublishedInstanceIdentity>(File.ReadAllText(_identityStatePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void DeletePublishedIdentity()
    {
        try
        {
            if (File.Exists(_identityStatePath))
                File.Delete(_identityStatePath);
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[SingleInstance] Failed to remove account identity state: {exception.Message}");
        }
    }
}
