#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace LuckyDogRise;

internal static class LegacyAccountStorageMigration
{
    public const string ExplicitDevImportArgument = "--import-shared-dev-storage";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static bool Prepare(AccountStorageContext storageContext)
    {
        if (BuildInfo.Channel == BuildChannel.Dev)
        {
            if (Array.Exists(OS.GetCmdlineUserArgs(), argument =>
                    string.Equals(argument, ExplicitDevImportArgument, StringComparison.OrdinalIgnoreCase)))
                return ImportSharedDevStorage(storageContext);
            return true;
        }

        ArchiveDiscardedUnscopedStorage();
        return true;
    }

    private static bool ImportSharedDevStorage(AccountStorageContext storageContext)
    {
        if (!string.Equals(storageContext.Provider, "steam", StringComparison.Ordinal))
        {
            GD.PushError("[AccountMigration] Shared Dev storage can only be imported into a verified Steam account.");
            return false;
        }
        if (HasAnyAccountScopedFile(storageContext))
        {
            GD.PushError(
                $"[AccountMigration] Target account storage already contains data at {storageContext.RootPath}; shared Dev import was skipped.");
            return false;
        }

        if (!SaveManager.TryImportUnscopedV15(storageContext, out var saveMessage))
        {
            GD.PushError($"[AccountMigration] {saveMessage}");
            return false;
        }

        EnsureDirectory(storageContext.RootPath);
        if (!ImportPlayerProgress(storageContext)
            || !CopyIfPresent("user://progress.cfg", storageContext.ProgressionPath)
            || !ArchiveUnscopedFiles("dev_import"))
            return false;
        GD.Print($"[AccountMigration] {saveMessage}");
        GD.Print($"[AccountMigration] Shared Dev storage was assigned to {storageContext} by explicit launch argument.");
        return true;
    }

    private static bool ImportPlayerProgress(AccountStorageContext storageContext)
    {
        const string source = "user://player_progress_0.json";
        if (!Godot.FileAccess.FileExists(source) || Godot.FileAccess.FileExists(storageContext.PlayerProgressPath))
            return true;

        try
        {
            var profile = JsonSerializer.Deserialize<PlayerProgressProfile>(
                File.ReadAllText(ProjectSettings.GlobalizePath(source)),
                JsonOptions);
            if (profile == null || profile.Version > 2)
                throw new InvalidDataException("Shared PlayerProgress is not an importable V1/V2 profile.");
            profile.Version = PlayerProgressProfile.CurrentVersion;
            profile.OwnerProvider = storageContext.Provider;
            profile.OwnerAccountId = storageContext.AccountId;
            profile.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(
                ProjectSettings.GlobalizePath(storageContext.PlayerProgressPath),
                JsonSerializer.Serialize(profile, JsonOptions));
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"[AccountMigration] PlayerProgress import failed: {exception.Message}");
            return false;
        }
    }

    private static void ArchiveDiscardedUnscopedStorage()
    {
        if (!HasAnyUnscopedFile())
            return;
        _ = ArchiveUnscopedFiles("discarded");
        GD.PushWarning("[AccountMigration] Unsupported unscoped channel storage was archived and was not assigned to any Steam account.");
    }

    private static bool HasAnyUnscopedFile()
    {
        foreach (var path in UnscopedFiles())
        {
            if (Godot.FileAccess.FileExists(path))
                return true;
        }
        return false;
    }

    private static bool HasAnyAccountScopedFile(AccountStorageContext storageContext) =>
        Godot.FileAccess.FileExists(storageContext.SavePath)
        || Godot.FileAccess.FileExists(storageContext.SaveBackupPath)
        || Godot.FileAccess.FileExists(storageContext.PlayerProgressPath)
        || Godot.FileAccess.FileExists(storageContext.PlayerProgressBackupPath)
        || Godot.FileAccess.FileExists(storageContext.PlayerProgressTempPath)
        || Godot.FileAccess.FileExists(storageContext.PlayerProgressCorruptPath)
        || Godot.FileAccess.FileExists(storageContext.ProgressionPath)
        || Godot.FileAccess.FileExists(storageContext.ProgressionBackupPath)
        || Godot.FileAccess.FileExists(storageContext.ProgressionTempPath)
        || Godot.FileAccess.FileExists(storageContext.ProgressionCorruptPath);

    private static bool ArchiveUnscopedFiles(string reason)
    {
        var archiveRoot = $"user://legacy_shared/{reason}_{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}";
        EnsureDirectory(archiveRoot);
        var succeeded = true;
        foreach (var source in UnscopedFiles())
        {
            if (!Godot.FileAccess.FileExists(source))
                continue;
            var destination = $"{archiveRoot}/{Path.GetFileName(ProjectSettings.GlobalizePath(source))}";
            try
            {
                File.Move(
                    ProjectSettings.GlobalizePath(source),
                    ProjectSettings.GlobalizePath(destination),
                    overwrite: false);
            }
            catch (Exception exception)
            {
                succeeded = false;
                GD.PushError($"[AccountMigration] Failed to archive {source}: {exception.Message}");
            }
        }
        return succeeded;
    }

    private static IEnumerable<string> UnscopedFiles()
    {
        yield return "user://saves/profile_0.json";
        yield return "user://saves/profile_0.backup.json";
        yield return "user://player_progress_0.json";
        yield return "user://player_progress_0.backup.json";
        yield return "user://progress.cfg";
    }

    private static bool CopyIfPresent(string source, string destination)
    {
        if (!Godot.FileAccess.FileExists(source) || Godot.FileAccess.FileExists(destination))
            return true;
        try
        {
            File.Copy(ProjectSettings.GlobalizePath(source), ProjectSettings.GlobalizePath(destination), overwrite: false);
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"[AccountMigration] Failed to copy {source}: {exception.Message}");
            return false;
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (!DirAccess.DirExistsAbsolute(path))
            DirAccess.MakeDirRecursiveAbsolute(path);
    }
}
