#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace LuckyDogRise;

public static class DiagnosticLog
{
    private const string DiagnosticDir = "user://diagnostics";
    private const string DiagnosticExportFallbackDir = "user://diagnostic-exports";
    private const int RetainedSessionFiles = 5;
    private const long MaxSessionBytes = 2 * 1024 * 1024;
    private const int MaxPersonaFileNameRunes = 48;
    private const uint KnownFolderFlagCreate = 0x00008000;
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string? _sessionPath;
    private static string _sessionId = string.Empty;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_sessionPath != null)
                return;

            var directory = ProjectSettings.GlobalizePath(DiagnosticDir);
            Directory.CreateDirectory(directory);
            _sessionId = Guid.NewGuid().ToString("N")[..12];
            _sessionPath = Path.Combine(
                directory,
                $"events-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{_sessionId}.jsonl");
            RotateSessionFiles(directory);
        }

        Record("session_started", new Dictionary<string, object?>
        {
            ["version"] = BuildInfo.DisplayVersion,
            ["channel"] = BuildInfo.Channel.ToString(),
            ["os"] = OS.GetName(),
        });
    }

    public static void Record(string eventName, object? data = null)
    {
        try
        {
            Initialize();
            lock (Sync)
            {
                if (_sessionPath == null)
                    return;
                var file = new FileInfo(_sessionPath);
                if (file.Exists && file.Length >= MaxSessionBytes)
                    return;

                var envelope = new Dictionary<string, object?>
                {
                    ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["sessionId"] = _sessionId,
                    ["eventName"] = eventName,
                    ["data"] = data,
                };
                var line = JsonSerializer.Serialize(envelope);
                File.AppendAllText(_sessionPath, line + System.Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[Diagnostics] Failed to record {eventName}: {exception.Message}");
        }
    }

    public static string ExportPackage(
        GameData? gameData,
        IGamePlatformService? platformService,
        string? exportDirectoryOverride = null)
    {
        Initialize();
        Record("diagnostics_export_requested");

        var exportDirectory = !string.IsNullOrWhiteSpace(exportDirectoryOverride)
            ? exportDirectoryOverride
            : ResolveExportDirectory();
        Directory.CreateDirectory(exportDirectory);
        var outputPath = GetAvailableExportPath(exportDirectory, platformService?.PersonaName);
        var temporaryPath = Path.Combine(
            exportDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        var skippedLogs = new List<string>();

        try
        {
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                var diagnosticDirectory = ProjectSettings.GlobalizePath(DiagnosticDir);
                foreach (var file in Directory.GetFiles(diagnosticDirectory, "events-*.jsonl")
                             .OrderByDescending(File.GetLastWriteTimeUtc)
                             .Take(RetainedSessionFiles))
                {
                    AddTextEntryWithSharedRead(archive, file, $"events/{Path.GetFileName(file)}", sanitize: false);
                }

                var logDirectory = ProjectSettings.GlobalizePath("user://logs");
                if (Directory.Exists(logDirectory))
                {
                    foreach (var file in Directory.GetFiles(logDirectory, "godot*.log")
                                 .OrderByDescending(File.GetLastWriteTimeUtc)
                                 .Take(RetainedSessionFiles))
                    {
                        try
                        {
                            AddTextEntryWithSharedRead(archive, file, $"logs/{Path.GetFileName(file)}", sanitize: true);
                        }
                        catch (Exception exception)
                        {
                            skippedLogs.Add(Path.GetFileName(file));
                            GD.PushWarning($"[Diagnostics] Skipped log {file}: {exception.Message}");
                        }
                    }
                }

                var summary = new Dictionary<string, object?>
                {
                    ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["version"] = BuildInfo.DisplayVersion,
                    ["channel"] = BuildInfo.Channel.ToString(),
                    ["operatingSystem"] = OS.GetName(),
                    ["platformProvider"] = platformService?.ProviderName ?? "Unavailable",
                    ["platformAvailable"] = platformService?.IsAvailable ?? false,
                    ["platformConnectionState"] = (platformService as IRecoverablePlatformService)?.ConnectionState.ToString(),
                    ["platformInventoryTrustState"] = (platformService as IRecoverablePlatformService)?.InventoryTrustState.ToString(),
                    ["platformInventoryTrustMessage"] = (platformService as IRecoverablePlatformService)?.InventoryTrustMessage,
                    ["chips"] = gameData?.Chips,
                    ["totalPlaySeconds"] = gameData?.TotalPlaySeconds,
                    ["ownedItemTypeCount"] = gameData?.Inventory.GetOwnedIds().Count(),
                    ["pendingLinkTreeClaim"] = gameData?.PendingLinkTreeClaim != null,
                    ["linkTreeRewardLedgerInitialized"] = gameData?.LinkTreeRewardLedgerInitialized,
                    ["pendingBlindBoxReward"] = gameData?.PendingBlindBoxReward != null,
                    ["pendingRecoveredItemTypeCount"] =
                        gameData?.GetRecoveredItemCounts().Count ?? 0,
                    ["pendingBlindBoxPreparation"] = gameData?.ActiveBlindBoxPreparationPending == true,
                    ["pendingBlindBoxCompletionReceiptItemDefId"] =
                        gameData?.PendingBlindBoxCompletionReceiptItemDefId ?? 0,
                    ["skippedLogFiles"] = skippedLogs,
                };
                var summaryEntry = archive.CreateEntry("diagnostic-summary.json");
                using var writer = new StreamWriter(summaryEntry.Open(), new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(summary, JsonOptions));
            }

            File.Move(temporaryPath, outputPath);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }

        return outputPath;
    }

    public static void RevealInExplorer(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            OS.ShellOpen(Path.GetDirectoryName(path) ?? path);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    private static void RotateSessionFiles(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "events-*.jsonl")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(RetainedSessionFiles - 1))
        {
            File.Delete(file);
        }
    }

    private static string ResolveExportDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var folderId = DownloadsFolderId;
                var pathPointer = IntPtr.Zero;
                try
                {
                    var result = SHGetKnownFolderPath(
                        ref folderId,
                        KnownFolderFlagCreate,
                        IntPtr.Zero,
                        out pathPointer);
                    if (result == 0 && pathPointer != IntPtr.Zero)
                    {
                        var downloads = Marshal.PtrToStringUni(pathPointer);
                        if (!string.IsNullOrWhiteSpace(downloads))
                        {
                            Directory.CreateDirectory(downloads);
                            return downloads;
                        }
                    }
                }
                finally
                {
                    if (pathPointer != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(pathPointer);
                }
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[Diagnostics] Windows Downloads folder is unavailable: {exception.Message}");
            }
        }

        var fallback = ProjectSettings.GlobalizePath(DiagnosticExportFallbackDir);
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static string GetAvailableExportPath(string directory, string? personaName)
    {
        var personaSegment = GetSafePersonaFileNameSegment(personaName);
        var stem = $"LDR_Diagnostics_{personaSegment}_{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        var candidate = Path.Combine(directory, $"{stem}.zip");
        for (var suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{stem}-{suffix}.zip");
        return candidate;
    }

    private static string GetSafePersonaFileNameSegment(string? personaName)
    {
        if (string.IsNullOrWhiteSpace(personaName))
            return "SteamPlayerUnavailable";

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(personaName.Length);
        var previousWasReplacement = false;
        foreach (var rune in personaName.Trim().EnumerateRunes())
        {
            var isInvalid = rune.Value <= char.MaxValue &&
                            Array.IndexOf(invalidCharacters, (char)rune.Value) >= 0;
            if (isInvalid || Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
            {
                if (!previousWasReplacement)
                    sanitized.Append('-');
                previousWasReplacement = true;
                continue;
            }

            sanitized.Append(rune.ToString());
            previousWasReplacement = false;
        }

        var safeRunes = sanitized.ToString()
            .Trim(' ', '.')
            .EnumerateRunes()
            .Take(MaxPersonaFileNameRunes)
            .ToArray();
        return safeRunes.Length == 0
            ? "SteamPlayerUnavailable"
            : string.Concat(safeRunes.Select(rune => rune.ToString()));
    }

    private static void AddTextEntryWithSharedRead(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        bool sanitize)
    {
        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            System.IO.FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var contents = reader.ReadToEnd();
        if (sanitize)
            contents = SanitizeLog(contents);

        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }

    private static string SanitizeLog(string contents)
    {
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            contents = contents.Replace(userProfile, "<USER_PROFILE>", StringComparison.OrdinalIgnoreCase);

        var userName = System.Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
            contents = contents.Replace(userName, "<USER_NAME>", StringComparison.OrdinalIgnoreCase);

        var machineName = System.Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName))
            contents = contents.Replace(machineName, "<MACHINE_NAME>", StringComparison.OrdinalIgnoreCase);

        contents = Regex.Replace(contents, @"\b7656119\d{10}\b", "<STEAM_ID>");
        return contents;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
