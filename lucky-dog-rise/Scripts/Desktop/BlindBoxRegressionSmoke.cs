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
            VerifyFallbackAdvanceAndLateReward(service);
            VerifyPreparedRewardInventoryVisibilityGrace();
            VerifySaveSnapshotIsolation();
            GD.Print("[BlindBoxRegressionSmoke] Passed fallback advance, late reward, inventory visibility grace, and save snapshot isolation checks.");
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
        Assert(state.PendingPreparation is
            {
                IsLate: true,
                StopRetryAfterFallback: true,
            }, "The empty preparation was not retained for one final no-retry inventory check.");
        service.CompleteClaimedPresentation(state, FirstScheduleId, completedSchedule);
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

        state.PreparedReward = new PreparedBlindBoxReward
        {
            ScheduleId = FirstScheduleId,
            BlindBoxId = FirstSteamBlindBoxId,
            PlatformInstanceId = 1,
            SteamItemDefId = 101002,
            ItemId = 1002,
            IsLate = true,
        };

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
            PendingPreparation = new PendingBlindBoxPreparation
            {
                ScheduleId = FirstScheduleId,
                BlindBoxId = FirstSteamBlindBoxId,
                GeneratorItemDefId = 700101,
                Phase = BlindBoxPreparationPhase.RevalidationRequired,
                IsLate = true,
                StopRetryAfterFallback = true,
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
        Assert(normalizedSnapshot.BlindBoxRuntimeState.PendingPreparation is
            {
                IsLate: true,
                StopRetryAfterFallback: true,
            }, "Save normalization discarded the skipped-preparation no-retry state.");

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
