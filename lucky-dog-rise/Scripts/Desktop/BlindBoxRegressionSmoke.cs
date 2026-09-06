#if DEBUG
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise;

internal static class BlindBoxRegressionSmoke
{
    private const int FirstScheduleId = 1001;
    private const int FirstSteamBlindBoxId = 1001;
    private const int FallbackBlindBoxId = 2001;

    public static void Run()
    {
        var gameData = new GameData();
        try
        {
            var service = new BlindBoxService(gameData);
            VerifyRetiredEyewearSaveCleanup();
            GD.Print("[BlindBoxRegressionSmoke] Passed retired eyewear save cleanup checks.");
            VerifySequenceProgressMigrationAndOrdering(service);
            VerifyDemoScheduleAndNonRepeatingPools(service);
            VerifyDeveloperLauncherGameplayChannel();
            VerifyFallbackAdvanceAndLateReward(service);
            VerifyPreparedRewardInventoryVisibilityGrace();
            VerifySaveSnapshotIsolation();
            GD.Print("[BlindBoxRegressionSmoke] Passed channel schedules, Demo pricing and non-repeating pools, linked progress, fallback/late rewards, save isolation, and compatibility checks.");
        }
        finally
        {
            gameData.Free();
        }
    }

    private static void VerifyDeveloperLauncherGameplayChannel()
    {
        BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Demo);
        BlindBoxService.ConfigureDebugScheduleChannel(DebugGameplayChannel.Demo);
        try
        {
            var schedules = BlindBoxService.GetSequenceSchedules();
            Assert(schedules.Count == 16 && schedules[0].Id == 3001,
                "The developer launcher Demo selection did not activate the Demo schedule chain.");
            Assert(BuildInfo.IsDebugDemo
                   && BuildCapabilities.CollectionEvents
                   && !BuildCapabilities.SteamInventory
                   && !BuildCapabilities.SteamCloud
                   && !BuildCapabilities.PlatformStatistics
                   && !BuildCapabilities.Achievements,
                "The developer launcher Demo selection did not activate the isolated capability profile.");
            var demoStorage = AccountStorageContext.ForDemoDebug("steam", "76561198000000000");
            Assert(demoStorage.RootPath == "user://accounts/demo-debug/steam-76561198000000000",
                "The Demo debug channel did not create an isolated account storage namespace.");
        }
        finally
        {
            BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Standard);
            BlindBoxService.ConfigureDebugScheduleChannel(DebugGameplayChannel.Standard);
        }

        Assert(BlindBoxService.GetSequenceSchedules()[0].Id == FirstScheduleId,
            "Restoring the developer launcher default did not reactivate the production schedule chain.");
    }

    private static void VerifyDemoScheduleAndNonRepeatingPools(BlindBoxService service)
    {
        const int normalDemoBlindBoxId = 2002;
        const int goodDemoBlindBoxId = 2003;
        var schedules = BlindBoxService.GetSequenceSchedules(DataTables.EBuildChannelMask.Demo);
        Assert(schedules.Count == 16,
            "The Demo first-run sequence does not contain the configured 16 Schedules.");
        Assert(schedules.Count(schedule => schedule.BlindBoxId == normalDemoBlindBoxId) == 13,
            "The Demo sequence does not contain 13 normal blind-box presentations.");
        Assert(schedules.Count(schedule => schedule.BlindBoxId == goodDemoBlindBoxId) == 3,
            "The Demo sequence does not contain 3 good blind-box presentations.");
        Assert(!LubanData.Tables.TbBlindBoxSchedule.DataList.Any(schedule =>
                schedule.IsEnabled
                && schedule.IsLoopTrack
                && (schedule.BuildChannelMask & DataTables.EBuildChannelMask.Demo) != 0),
            "The finite Demo collection unexpectedly contains a loop Schedule.");

        var normalDemoBox = LubanData.Tables.TbBlindBox.GetOrDefault(normalDemoBlindBoxId)
            ?? throw new InvalidOperationException("Missing normal Demo blind box.");
        var firstPrice = service.ResolvePrice(schedules[0], normalDemoBox, useScheduleOverride: true);
        var secondPrice = service.ResolvePrice(schedules[1], normalDemoBox, useScheduleOverride: true);
        Assert(firstPrice.ActualCost == 0 && firstPrice.StrikeThrough,
            "The first Demo blind box is not using its configured free price override.");
        Assert(secondPrice.ActualCost > 0 && !secondPrice.StrikeThrough,
            "The second Demo blind box is not inheriting its normal chip cost.");
        var poolItemIds = LubanData.Tables.TbBlindBoxItemWeight.DataList
            .Where(entry => entry.IsEnabled
                            && entry.BlindBoxId == normalDemoBlindBoxId
                            && entry.Weight > 0)
            .Select(entry => entry.ItemId)
            .Distinct()
            .ToList();
        Assert(poolItemIds.Count == 13,
            "The normal Demo blind box does not contain the configured 13-item collection.");

        var remainingItemId = poolItemIds[^1];
        var state = new BlindBoxRuntimeState
        {
            DrawnItemIdsByBlindBoxId = new Dictionary<int, List<int>>
            {
                [normalDemoBlindBoxId] = poolItemIds.Take(poolItemIds.Count - 1).ToList(),
            },
        };
        Assert(service.RollRewardForTesting(normalDemoBox, state)?.Id == remainingItemId,
            "ExcludeDrawnInThisBox did not renormalize rarity weights around the final remaining item.");
        Assert(service.RecordClaimedReward(state, normalDemoBlindBoxId, remainingItemId),
            "Claiming the final Demo item did not update the per-box draw history.");
        Assert(service.RollRewardForTesting(normalDemoBox, state) == null,
            "An exhausted ExcludeDrawnInThisBox pool still returned a reward.");

        var goodDemoBox = LubanData.Tables.TbBlindBox.GetOrDefault(goodDemoBlindBoxId)
            ?? throw new InvalidOperationException("Missing good Demo blind box.");
        Assert(service.RollRewardForTesting(goodDemoBox, state) != null,
            "Draw history from one Demo blind box leaked into another blind box.");

        var normalRepeatableBox = LubanData.Tables.TbBlindBox.DataList.First(box =>
            box.IsEnabled && box.RewardSelectionMode == DataTables.ERewardSelectionMode.Normal);
        var repeatablePoolIds = LubanData.Tables.TbBlindBoxItemWeight.DataList
            .Where(entry => entry.IsEnabled && entry.BlindBoxId == normalRepeatableBox.Id)
            .Select(entry => entry.ItemId)
            .Distinct()
            .ToList();
        var repeatableState = new BlindBoxRuntimeState
        {
            DrawnItemIdsByBlindBoxId = new Dictionary<int, List<int>>
            {
                [normalRepeatableBox.Id] = repeatablePoolIds,
            },
        };
        Assert(service.RollRewardForTesting(normalRepeatableBox, repeatableState) != null,
            "A Normal blind box incorrectly honored ExcludeDrawnInThisBox history.");
    }

    private static void VerifySequenceProgressMigrationAndOrdering(BlindBoxService service)
    {
        var schedules = BlindBoxService.GetSequenceSchedules();
        Assert(schedules.Count > 0 && schedules[0].Id == FirstScheduleId,
            "The linked first-run reward sequence did not start at Schedule 1001.");
        for (var index = 0; index < schedules.Count - 1; index++)
        {
            Assert(schedules[index].NextScheduleId == schedules[index + 1].Id,
                $"Schedule {schedules[index].Id} did not link to the next runtime Schedule.");
            Assert(schedules[index].ProgressCheckpoint < schedules[index + 1].ProgressCheckpoint,
                "First-run reward progress checkpoints were not strictly increasing.");
        }
        Assert(schedules[^1].NextScheduleId == 0,
            "The final first-run reward Schedule did not terminate the linked sequence.");

        var legacyState = new BlindBoxRuntimeState { SequenceIndex = 5 };
        Assert(service.NormalizeSequenceProgress(legacyState) == schedules[4].ProgressCheckpoint,
            "A pre-checkpoint local save did not migrate its exact completed Schedule.");
        Assert(legacyState.SequenceIndex == 5,
            "Migrating a pre-checkpoint local save changed its current sequence position.");

        var restoredState = new BlindBoxRuntimeState
        {
            SequenceProgressCheckpoint = schedules[2].ProgressCheckpoint,
        };
        service.NormalizeSequenceProgress(restoredState);
        Assert(restoredState.SequenceIndex == 3,
            "A stable progress checkpoint did not restore the next linked Schedule.");
        Assert(service.MergeSequenceProgressCheckpoint(
                   restoredState,
                   schedules[4].ProgressCheckpoint)
               && restoredState.SequenceIndex == 5,
            "A higher Steam progress checkpoint did not advance the linked sequence.");
    }

    private static void VerifyPreparedRewardInventoryVisibilityGrace()
    {
        var reward = new PreparedBlindBoxReward
        {
            ScheduleId = FirstScheduleId,
            BlindBoxId = FirstSteamBlindBoxId,
            PlatformInstanceId = 1,
            SteamItemDefId = 102009,
            ItemId = 2009,
        };
        reward.MarkConfirmed(2.0);

        Assert(!reward.MarkMissingAndShouldDiscard(2.1),
            "A freshly confirmed Steam reward was discarded by the first stale inventory snapshot.");
        Assert(!reward.MarkMissingAndShouldDiscard(31.9),
            "A Steam reward was discarded before the inventory visibility grace elapsed.");
        Assert(reward.MarkMissingAndShouldDiscard(32.1),
            "A persistently missing Steam reward was not discarded after repeated snapshots and the grace period.");

        reward.MarkPresent();
        Assert(reward.ConsecutiveMissingInventorySnapshots == 0
               && reward.FirstMissingAtTotalPlaySeconds == null,
            "Seeing the Steam instance again did not reset its missing-inventory evidence.");
    }

    private static void VerifyFallbackAdvanceAndLateReward(BlindBoxService service)
    {
        var state = new BlindBoxRuntimeState();
        Assert(service.TryGetCurrentSchedule(state, out var firstSchedule)
               && firstSchedule?.Id == FirstScheduleId,
            "The first newcomer Schedule is unavailable for the regression check.");
        var presentationSeconds = (double)firstSchedule!.StartSeconds;
        state.PendingPreparation = new PendingBlindBoxPreparation
        {
            ScheduleId = FirstScheduleId,
            BlindBoxId = FirstSteamBlindBoxId,
            GeneratorItemDefId = firstSchedule.SteamPlaytimeGeneratorItemDefId,
            Phase = BlindBoxPreparationPhase.RetryWaiting,
        };
        state.ScheduleSeconds = presentationSeconds;
        Assert(service.MaintainPresentation(state),
            "The first Fallback was not locked at its presentation point.");
        Assert(state.LockedPresentation is
            {
                ScheduleId: FirstScheduleId,
                BlindBoxId: FallbackBlindBoxId,
                Kind: LockedBlindBoxPresentationKind.Fallback,
            }, "The first Fallback locked unexpected presentation data.");

        var completedSchedule = service.ConsumeOpenedPresentation(state);
        Assert(completedSchedule,
            "Claiming a Fallback did not complete the Steam newcomer Schedule.");
        Assert(state.SequenceIndex == 1,
            "Claiming a Fallback did not advance the newcomer sequence.");
        Assert(state.SequenceProgressCheckpoint == 0,
            "Opening a Fallback recorded stable progress before the reward was claimed.");
        Assert(state.PendingPreparation is
            {
                IsLate: true,
                StopRetryAfterFallback: true,
            }, "The empty preparation was not retained for one final no-retry inventory check.");
        service.CompleteClaimedPresentation(state, FirstScheduleId, completedSchedule);
        Assert(state.SequenceProgressCheckpoint == firstSchedule.ProgressCheckpoint,
            "Claiming a Fallback did not record the completed Schedule progress checkpoint.");
        Assert(Math.Abs(state.LastClaimSeconds - presentationSeconds) < 0.001,
            "The Fallback claim time was not recorded.");

        var inFlightState = new BlindBoxRuntimeState
        {
            PendingPreparation = new PendingBlindBoxPreparation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FirstSteamBlindBoxId,
                GeneratorItemDefId = firstSchedule.SteamPlaytimeGeneratorItemDefId,
                Phase = BlindBoxPreparationPhase.Submitted,
            },
            LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FallbackBlindBoxId,
                Kind = LockedBlindBoxPresentationKind.Fallback,
            },
        };
        Assert(service.ConsumeOpenedPresentation(inFlightState),
            "An in-flight Fallback did not complete the Steam newcomer Schedule.");
        Assert(inFlightState.PendingPreparation is
            {
                IsLate: true,
                StopRetryAfterFallback: true,
            }, "The in-flight preparation was not retained as a single-settlement late request.");

        state.GeneratorActivation = new PlaytimeGeneratorActivationState
        {
            DeferredReward = new PreparedBlindBoxReward
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FirstSteamBlindBoxId,
                PlatformInstanceId = 1,
                SteamItemDefId = 101002,
                ItemId = 1002,
            },
        };
        Assert(service.TryPromoteDeferredActivationReward(state, out var promotedAsLate)
               && promotedAsLate,
            "An activation reward for an already skipped newcomer Schedule was not promoted as late.");
        Assert(state.PreparedReward is { ScheduleId: FirstScheduleId, IsLate: true }
               && state.GeneratorActivation.DeferredReward == null,
            "Promoting a skipped activation reward did not release its deferred slot.");

        Assert(service.TryGetCurrentSchedule(state, out var secondSchedule)
               && secondSchedule != null && secondSchedule.Id != FirstScheduleId,
            "The second newcomer Schedule is unavailable for the late-reward check.");
        var secondIntervalSeconds = (double)secondSchedule!.IntervalSeconds;

        state.ScheduleSeconds = state.LastClaimSeconds + secondIntervalSeconds - 0.01;
        Assert(!service.MaintainPresentation(state) && state.LockedPresentation == null,
            "A late Steam reward appeared before the next normal presentation point.");

        state.ScheduleSeconds = state.LastClaimSeconds + secondIntervalSeconds;
        Assert(service.MaintainPresentation(state),
            "A late Steam reward did not appear at the next normal presentation point.");
        Assert(state.LockedPresentation is
            {
                ScheduleId: FirstScheduleId,
                BlindBoxId: FirstSteamBlindBoxId,
                Kind: LockedBlindBoxPresentationKind.LateSteam,
            }, "The late Steam reward was misattributed to the next newcomer Schedule.");
        Assert(!service.ConsumeOpenedPresentation(state),
            "Claiming the late Steam reward incorrectly completed the next newcomer Schedule.");
        Assert(state.SequenceIndex == 1,
            "Claiming the late Steam reward changed newcomer progress.");
        Assert(state.SequenceProgressCheckpoint == firstSchedule.ProgressCheckpoint,
            "Claiming the late Steam reward changed the stable progress checkpoint.");
    }

    private static void VerifySaveSnapshotIsolation()
    {
        var liveState = new BlindBoxRuntimeState
        {
            SequenceIndex = 0,
            SequenceProgressCheckpoint = 0,
            ScheduleSeconds = 1.0,
            LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FallbackBlindBoxId,
                Kind = LockedBlindBoxPresentationKind.Fallback,
            },
            PendingPreparation = new PendingBlindBoxPreparation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FirstSteamBlindBoxId,
                GeneratorItemDefId = 700101,
                Phase = BlindBoxPreparationPhase.RevalidationRequired,
                IsLate = true,
                StopRetryAfterFallback = true,
            },
            DrawnItemIdsByBlindBoxId = new Dictionary<int, List<int>>
            {
                [2002] = [2003, 2009],
            },
        };
        var liveProfile = new SaveProfile
        {
            BlindBoxRuntimeState = liveState,
            CollectionEventRevealedItemIdsByEvent = new Dictionary<string, List<int>>
            {
                ["event-smoke"] = [2009, 2009, -1],
            },
        };

        var normalizedSnapshot = SaveManager.CreateNormalizedDetachedSnapshotForTesting(liveProfile);
        Assert(!ReferenceEquals(liveProfile, normalizedSnapshot),
            "Save preparation reused the live profile object.");
        Assert(!ReferenceEquals(liveState, normalizedSnapshot.BlindBoxRuntimeState),
            "Save preparation reused the live blind-box runtime object.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.SequenceProgressCheckpoint == 0,
            "Save normalization changed the stable sequence progress checkpoint.");
        Assert(liveState.LockedPresentation != null,
            "Save normalization cleared the live locked presentation.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.LockedPresentation is
            {
                ScheduleId: FirstScheduleId,
                BlindBoxId: FallbackBlindBoxId,
                Kind: LockedBlindBoxPresentationKind.Fallback,
            }, "Save normalization discarded a valid newcomer locked presentation.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.PendingPreparation is
            {
                IsLate: true,
                StopRetryAfterFallback: true,
            }, "Save normalization discarded the skipped-preparation no-retry state.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.DrawnItemIdsByBlindBoxId?
                   .GetValueOrDefault(2002)?.SequenceEqual([2003, 2009]) == true,
            "Save normalization discarded valid per-box Demo draw history.");
        Assert(normalizedSnapshot.CollectionEventRevealedItemIdsByEvent?
                   .GetValueOrDefault("event-smoke")?.SequenceEqual([2009]) == true,
            "Save normalization did not preserve the independent collection-event reveal history.");

        normalizedSnapshot.BlindBoxRuntimeState.LockedPresentation = null;
        normalizedSnapshot.BlindBoxRuntimeState.DrawnItemIdsByBlindBoxId![2002].Add(2013);
        normalizedSnapshot.CollectionEventRevealedItemIdsByEvent!["event-smoke"].Add(2013);
        Assert(liveState.LockedPresentation != null,
            "Mutating the persistence snapshot changed the live locked presentation.");
        Assert(liveState.DrawnItemIdsByBlindBoxId[2002].SequenceEqual([2003, 2009]),
            "Mutating the persistence snapshot changed the live per-box draw history.");
        Assert(liveProfile.CollectionEventRevealedItemIdsByEvent["event-smoke"].SequenceEqual([2009, 2009, -1]),
            "Mutating the collection-event persistence snapshot changed the live reveal history.");

        var preparedProfile = new SaveProfile
        {
            BlindBoxRuntimeState = new BlindBoxRuntimeState
            {
                PreparedReward = new PreparedBlindBoxReward
                {
                    ScheduleId = FirstScheduleId,
                    BlindBoxId = FirstSteamBlindBoxId,
                    PlatformInstanceId = 1,
                    SteamItemDefId = 102009,
                    ItemId = 2009,
                    ConfirmedAtTotalPlaySeconds = 2.0,
                    FirstMissingAtTotalPlaySeconds = 2.1,
                    ConsecutiveMissingInventorySnapshots = 1,
                },
            },
        };
        var preparedSnapshot = SaveManager.CreateNormalizedDetachedSnapshotForTesting(preparedProfile);
        Assert(preparedSnapshot.BlindBoxRuntimeState.PreparedReward is
            {
                ConfirmedAtTotalPlaySeconds: 2.0,
                FirstMissingAtTotalPlaySeconds: 2.1,
                ConsecutiveMissingInventorySnapshots: 1,
            }, "Save normalization discarded prepared-reward inventory visibility evidence.");

        var recoveredProfile = new SaveProfile
        {
            OwnedItemCounts = new Dictionary<int, int> { [2009] = 2 },
            RecoveredItemCounts = new Dictionary<int, int> { [2009] = 3 },
            ExpectedPlatformItemIncreaseCounts = new Dictionary<int, int> { [2009] = 1 },
        };
        var recoveredSnapshot = SaveManager.CreateNormalizedDetachedSnapshotForTesting(recoveredProfile);
        Assert(recoveredSnapshot.RecoveredItemCounts?.GetValueOrDefault(2009) == 2,
            "Recovered-item notices were not clamped to the owned inventory quantity.");
        recoveredSnapshot.RecoveredItemCounts![2009] = 1;
        Assert(recoveredProfile.RecoveredItemCounts.GetValueOrDefault(2009) == 3,
            "Mutating the recovered-item persistence snapshot changed the live notice queue.");
        Assert(recoveredSnapshot.ExpectedPlatformItemIncreaseCounts?.GetValueOrDefault(2009) == 1,
            "A known platform acquisition marker was not retained in the persistence snapshot.");
        recoveredSnapshot.ExpectedPlatformItemIncreaseCounts![2009] = 2;
        Assert(recoveredProfile.ExpectedPlatformItemIncreaseCounts.GetValueOrDefault(2009) == 1,
            "Mutating a known platform acquisition marker changed the live save state.");
    }

    private static void VerifyRetiredEyewearSaveCleanup()
    {
        const int redShibaItemId = 1001;
        const int retiredEyewearItemId = 3001;
        if (LubanData.Tables.TbItem.GetOrDefault(retiredEyewearItemId) == null)
            return;

        var profile = new SaveProfile
        {
            OwnedItemCounts = new Dictionary<int, int>
            {
                [redShibaItemId] = 1,
                [retiredEyewearItemId] = 2,
            },
            OwnedItemIds = [redShibaItemId, retiredEyewearItemId],
            EquippedItemIdsByType = new Dictionary<string, int>
            {
                [DataTables.EItemType.Dog.ToString()] = redShibaItemId,
                [DataTables.EItemType.Eyewear.ToString()] = retiredEyewearItemId,
            },
            NewItemIds = [retiredEyewearItemId],
        };

        var normalized = SaveManager.CreateNormalizedDetachedSnapshotForTesting(profile);
        Assert(normalized.OwnedItemCounts.GetValueOrDefault(retiredEyewearItemId) == 2,
            "Retired eyewear ownership was removed instead of being preserved for Steam compatibility.");
        Assert(!normalized.EquippedItemIdsByType.Keys.Any(key =>
                Enum.TryParse<DataTables.EItemType>(key, ignoreCase: true, out var type)
                && type == DataTables.EItemType.Eyewear),
            "Retired eyewear remained equipped after save normalization.");
        Assert(!normalized.NewItemIds.Contains(retiredEyewearItemId),
            "A hidden retired eyewear item kept an inaccessible New marker.");
        Assert(normalized.EquippedItemIdsByType.GetValueOrDefault(
                   DataTables.EItemType.Dog.ToString()) == redShibaItemId,
            "Retired eyewear cleanup changed an unrelated equipped item.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
