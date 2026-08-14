#if DEBUG
#nullable enable

using System;
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
            VerifyRepeatedFallbackTiming(service);
            VerifyPreparedRewardInventoryVisibilityGrace();
            VerifySaveSnapshotIsolation();
            GD.Print("[BlindBoxRegressionSmoke] Passed fallback timing, inventory visibility grace, and save snapshot isolation checks.");
        }
        finally
        {
            gameData.Free();
        }
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

    private static void VerifyRepeatedFallbackTiming(BlindBoxService service)
    {
        var state = new BlindBoxRuntimeState();
        Assert(service.TryGetCurrentSchedule(state, out var firstSchedule)
               && firstSchedule?.Id == FirstScheduleId,
            "The first newcomer Schedule is unavailable for the regression check.");
        var presentationSeconds = (double)firstSchedule!.StartSeconds;
        var presentationIntervalSeconds = (double)firstSchedule.IntervalSeconds;

        for (var fallbackCount = 1; fallbackCount <= 8; fallbackCount++)
        {
            state.ScheduleSeconds = presentationSeconds;
            Assert(service.MaintainPresentation(state),
                $"Fallback {fallbackCount} was not locked at its presentation point.");
            Assert(state.LockedPresentation is
                {
                    ScheduleId: FirstScheduleId,
                    BlindBoxId: FallbackBlindBoxId,
                    Kind: LockedBlindBoxPresentationKind.Fallback,
                }, $"Fallback {fallbackCount} locked unexpected presentation data.");

            var completedSchedule = service.ConsumeOpenedPresentation(state);
            Assert(!completedSchedule,
                $"Fallback {fallbackCount} incorrectly completed the Steam newcomer Schedule.");
            service.CompleteClaimedPresentation(state, FirstScheduleId, completedSchedule);

            Assert(state.SequenceIndex == 0,
                $"Fallback {fallbackCount} incorrectly advanced the newcomer sequence.");
            Assert(Math.Abs(state.LastClaimSeconds - presentationSeconds) < 0.001,
                $"Fallback {fallbackCount} did not record its claim time.");

            state.ScheduleSeconds = presentationSeconds + 1.0;
            Assert(!service.MaintainPresentation(state) && state.LockedPresentation == null,
                $"Fallback {fallbackCount} immediately produced another balloon.");
            presentationSeconds += presentationIntervalSeconds;
        }

        state.PreparedReward = new PreparedBlindBoxReward
        {
            ScheduleId = FirstScheduleId,
            BlindBoxId = FirstSteamBlindBoxId,
            PlatformInstanceId = 1,
            SteamItemDefId = 101002,
            ItemId = 1002,
            IsLate = true,
        };

        state.ScheduleSeconds = state.LastClaimSeconds + presentationIntervalSeconds - 0.01;
        Assert(!service.MaintainPresentation(state) && state.LockedPresentation == null,
            "A late Steam reward appeared before the next normal presentation point.");

        state.ScheduleSeconds = state.LastClaimSeconds + presentationIntervalSeconds;
        Assert(service.MaintainPresentation(state),
            "A late Steam reward did not appear at the next normal presentation point.");
        Assert(state.LockedPresentation is
            {
                ScheduleId: FirstScheduleId,
                BlindBoxId: FirstSteamBlindBoxId,
                Kind: LockedBlindBoxPresentationKind.DeferredSequenceSteam,
            }, "The late Steam reward did not preserve the current newcomer Schedule.");
        Assert(service.ConsumeOpenedPresentation(state),
            "Claiming the late Steam reward did not complete its newcomer Schedule.");
        Assert(state.SequenceIndex == 1,
            "Claiming the late Steam reward did not advance to the next newcomer Schedule.");
    }

    private static void VerifySaveSnapshotIsolation()
    {
        var liveState = new BlindBoxRuntimeState
        {
            SequenceIndex = 0,
            ScheduleSeconds = 1.0,
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
            LockedPresentation = new LockedBlindBoxPresentation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FallbackBlindBoxId,
                Kind = LockedBlindBoxPresentationKind.Fallback,
            },
        };
        var liveProfile = new SaveProfile { BlindBoxRuntimeState = liveState };

        var normalizedSnapshot = SaveManager.CreateNormalizedDetachedSnapshotForTesting(liveProfile);
        Assert(!ReferenceEquals(liveProfile, normalizedSnapshot),
            "Save preparation reused the live profile object.");
        Assert(!ReferenceEquals(liveState, normalizedSnapshot.BlindBoxRuntimeState),
            "Save preparation reused the live blind-box runtime object.");
        Assert(liveState.LockedPresentation != null,
            "Save normalization cleared the live locked presentation.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.LockedPresentation is
            {
                ScheduleId: FirstScheduleId,
                BlindBoxId: FallbackBlindBoxId,
                Kind: LockedBlindBoxPresentationKind.Fallback,
            }, "Save normalization discarded a valid newcomer locked presentation.");
        Assert(normalizedSnapshot.BlindBoxRuntimeState.PreparedReward is
            {
                ConfirmedAtTotalPlaySeconds: 2.0,
                FirstMissingAtTotalPlaySeconds: 2.1,
                ConsecutiveMissingInventorySnapshots: 1,
            }, "Save normalization discarded prepared-reward inventory visibility evidence.");

        normalizedSnapshot.BlindBoxRuntimeState.LockedPresentation = null;
        Assert(liveState.LockedPresentation != null,
            "Mutating the persistence snapshot changed the live locked presentation.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
