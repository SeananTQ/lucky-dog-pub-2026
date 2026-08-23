#nullable enable

using System;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Stores small, non-economic lifecycle flags that belong to one platform account.
/// Machine preferences remain in SettingsManager and are intentionally not copied here.
/// </summary>
public sealed class AccountStateManager
{
    private const string SectionTutorialProgress = "tutorial_progress";

    public const int InitialMeetingTutorialId = 1001;

    private readonly AccountStorageContext _storageContext;

    public AccountStateManager(AccountStorageContext storageContext)
    {
        _storageContext = storageContext ?? throw new ArgumentNullException(nameof(storageContext));
    }

    public enum TutorialStepState
    {
        NotStarted = 0,
        Shown = 1,
        Completed = 2,
    }

    public TutorialStepState LoadTutorialStepState(int tutorialId)
    {
        var config = Load();
        return ParseState(config.GetValue(
            SectionTutorialProgress,
            GetTutorialStateKey(tutorialId),
            (int)TutorialStepState.NotStarted));
    }

    public void SaveTutorialStepState(int tutorialId, TutorialStepState state)
    {
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown tutorial step state.");

        var config = Load();
        config.SetValue(SectionTutorialProgress, GetTutorialStateKey(tutorialId), (int)state);
        Save(config);
    }

    /// <summary>
    /// Existing account data predating account_state.cfg is treated as already introduced.
    /// A genuinely new account remains NotStarted even when another account used this machine.
    /// </summary>
    public TutorialStepState LoadInitialMeetingStateForStartup()
    {
        var config = Load();
        var key = GetTutorialStateKey(InitialMeetingTutorialId);
        if (config.HasSectionKey(SectionTutorialProgress, key))
            return ParseState(config.GetValue(
                SectionTutorialProgress,
                key,
                (int)TutorialStepState.NotStarted));

        if (!HasExistingAccountData())
            return TutorialStepState.NotStarted;

        config.SetValue(SectionTutorialProgress, key, (int)TutorialStepState.Shown);
        Save(config);
        GD.Print($"[AccountState] Migrated existing account {_storageContext} as already introduced.");
        return TutorialStepState.Shown;
    }

    private bool HasExistingAccountData() =>
        FileAccess.FileExists(_storageContext.SavePath)
        || FileAccess.FileExists(_storageContext.SaveBackupPath)
        || FileAccess.FileExists(_storageContext.PlayerProgressPath)
        || FileAccess.FileExists(_storageContext.PlayerProgressBackupPath)
        || FileAccess.FileExists(_storageContext.ProgressionPath)
        || FileAccess.FileExists(_storageContext.ProgressionBackupPath);

    private ConfigFile Load()
    {
        var config = new ConfigFile();
        config.Load(_storageContext.AccountStatePath);
        return config;
    }

    private void Save(ConfigFile config)
    {
        var absoluteRoot = ProjectSettings.GlobalizePath(_storageContext.RootPath);
        DirAccess.MakeDirRecursiveAbsolute(absoluteRoot);
        var error = config.Save(_storageContext.AccountStatePath);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Failed to save account state for {_storageContext}: {error}.");
    }

    private static TutorialStepState ParseState(Variant value)
    {
        var numeric = (int)value;
        return Enum.IsDefined(typeof(TutorialStepState), numeric)
            ? (TutorialStepState)numeric
            : TutorialStepState.NotStarted;
    }

    private static string GetTutorialStateKey(int tutorialId) => $"tutorial_{tutorialId}";
}
