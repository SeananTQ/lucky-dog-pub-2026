using Godot;
using System;
using IOFile = System.IO.File;

namespace LuckyDogRise;

public enum PlayerRank
{
    HomeGame,    // 0 - bare arm
    CasinoReg,   // 1000 - dress shirt
    Tournament,  // 10000 - baseball jacket
    HighStakes,  // 100000 - suit
    Champion     // 1000000 - WSOP bracelet
}

public class ProgressionManager
{
    private readonly string _savePath;
    private readonly string _backupPath;
    private readonly string _tempPath;
    private readonly string _corruptPath;
    private bool _writesFrozen;

    public int HighScore { get; private set; }
    public PlayerRank CurrentRank { get; private set; }
    private PlayerRank _rankBeforeHand;

    private static readonly (PlayerRank rank, int threshold)[] Ranks = new[]
    {
        (PlayerRank.HomeGame, 0),
        (PlayerRank.CasinoReg, 1000),
        (PlayerRank.Tournament, 10000),
        (PlayerRank.HighStakes, 100000),
        (PlayerRank.Champion, 1000000),
    };

    public ProgressionManager(AccountStorageContext storageContext)
    {
        ArgumentNullException.ThrowIfNull(storageContext);
        _savePath = storageContext.ProgressionPath;
        _backupPath = storageContext.ProgressionBackupPath;
        _tempPath = storageContext.ProgressionTempPath;
        _corruptPath = storageContext.ProgressionCorruptPath;
        Load();
    }

    public void UpdateHighScore(int currentChips)
    {
        _rankBeforeHand = CurrentRank;
        if (currentChips > HighScore)
        {
            HighScore = currentChips;
            UpdateRank();
            Save();
        }
    }

    public bool CheckRankUp()
    {
        return CurrentRank > _rankBeforeHand;
    }

    public void Reset()
    {
        // Don't reset high score - it persists
        _rankBeforeHand = CurrentRank;
    }

    private void UpdateRank()
    {
        for (int i = Ranks.Length - 1; i >= 0; i--)
        {
            if (HighScore >= Ranks[i].threshold)
            {
                CurrentRank = Ranks[i].rank;
                return;
            }
        }
    }

    private void Save()
    {
        if (_writesFrozen)
            return;
        var config = new ConfigFile();
        config.SetValue("progress", "high_score", HighScore);
        var saveError = config.Save(_tempPath);
        if (saveError != Error.Ok)
        {
            GD.PushError($"[Progression] Failed to write temporary progress file: {saveError}.");
            return;
        }

        var verification = new ConfigFile();
        if (verification.Load(_tempPath) != Error.Ok)
        {
            GD.PushError("[Progression] Temporary progress file could not be verified.");
            return;
        }

        try
        {
            var save = ProjectSettings.GlobalizePath(_savePath);
            var backup = ProjectSettings.GlobalizePath(_backupPath);
            var temp = ProjectSettings.GlobalizePath(_tempPath);
            if (IOFile.Exists(save))
                IOFile.Replace(temp, save, backup, ignoreMetadataErrors: true);
            else
                IOFile.Move(temp, save);
        }
        catch (Exception exception)
        {
            GD.PushError($"[Progression] Failed to commit progress file: {exception.Message}");
        }
    }

    private void Load()
    {
        if (TryLoad(_savePath, out var highScore))
        {
            HighScore = highScore;
        }
        else if (FileAccess.FileExists(_savePath))
        {
            ArchiveCorruptPrimary();
            if (TryLoad(_backupPath, out highScore))
            {
                HighScore = highScore;
                try
                {
                    IOFile.Copy(
                        ProjectSettings.GlobalizePath(_backupPath),
                        ProjectSettings.GlobalizePath(_savePath),
                        overwrite: true);
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"[Progression] Backup loaded but primary restore failed: {exception.Message}");
                }
            }
        }
        else if (TryLoad(_backupPath, out highScore))
        {
            HighScore = highScore;
        }
        UpdateRank();
        _rankBeforeHand = CurrentRank;
    }

    private static bool TryLoad(string path, out int highScore)
    {
        highScore = 0;
        try
        {
            var config = new ConfigFile();
            if (config.Load(path) != Error.Ok)
                return false;
            highScore = Math.Max(0, (int)(long)config.GetValue("progress", "high_score", 0));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ArchiveCorruptPrimary()
    {
        try
        {
            var source = ProjectSettings.GlobalizePath(_savePath);
            var destination = ProjectSettings.GlobalizePath(_corruptPath);
            if (IOFile.Exists(destination))
                destination = $"{destination}.{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
            IOFile.Move(source, destination, overwrite: false);
            GD.PushWarning($"[Progression] Corrupt progress file was archived: {destination}");
        }
        catch (Exception exception)
        {
            GD.PushError($"[Progression] Failed to archive corrupt progress file: {exception.Message}");
        }
    }

    public void FreezeWrites() => _writesFrozen = true;
}
