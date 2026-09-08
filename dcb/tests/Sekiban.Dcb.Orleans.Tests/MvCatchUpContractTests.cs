using System.Reflection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed class MvCatchUpContractTests
{
    [Fact]
    public void MissingHintBudget_CountsOnlyAgedNewerEmptyOrUnsafeObservations()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var agedHint = SortableUniqueId.Generate(now.UtcDateTime.AddSeconds(-10), Guid.Empty);
        var freshHint = SortableUniqueId.Generate(now.UtcDateTime.AddSeconds(-1), Guid.Empty);
        var budget = new MvCatchUpStallBudget(TimeSpan.FromSeconds(5), () => now);

        Assert.True(MvCatchUpStallBudget.IsSafeEligible(agedHint, safeWindowMs: 5_000, now));
        Assert.False(MvCatchUpStallBudget.IsSafeEligible(freshHint, safeWindowMs: 5_000, now));

        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        now = now.AddSeconds(4);
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.UnsafeWindow, safeEligible: true));
        now = now.AddSeconds(2);
        Assert.True(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        Assert.Equal(agedHint, budget.HintSortableUniqueId);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero), budget.FirstObservedAtUtc);

        budget.Reset();
        now = new DateTimeOffset(2026, 9, 7, 13, 0, 0, TimeSpan.Zero);

        // Duplicate/current, fresh, unsafe-before-safe, and progressed observations do not consume the budget.
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: false, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        Assert.False(budget.Observe(freshHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: false));
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.UnsafeWindow, safeEligible: false));
        now = now.AddSeconds(20);
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 1, MvCatchUpOutcome.Progressed, safeEligible: true));
    }

    [Fact]
    public void MissingHintBudget_DoesNotAgeAcrossFailedReadOrRetryableFailure()
    {
        var now = new DateTimeOffset(2026, 9, 7, 14, 0, 0, TimeSpan.Zero);
        var agedHint = SortableUniqueId.Generate(now.UtcDateTime.AddSeconds(-10), Guid.Empty);
        var budget = new MvCatchUpStallBudget(TimeSpan.FromSeconds(5), () => now);

        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        now = now.AddSeconds(4);

        budget.PauseAfterFailure();
        now = now.AddSeconds(30);
        Assert.Equal(agedHint, budget.HintSortableUniqueId);
        Assert.Null(budget.FirstObservedAtUtc);
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.UnsafeWindow, safeEligible: true));
        Assert.Equal(now, budget.FirstObservedAtUtc);

        now = now.AddSeconds(4);
        Assert.False(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
        now = now.AddSeconds(1);
        Assert.True(budget.Observe(agedHint, newerHintOutstanding: true, appliedEvents: 0, MvCatchUpOutcome.Empty, safeEligible: true));
    }

    [Fact]
    public void CatchUpResults_DistinguishWaitingRetryableAndPermanentOutcomes_WithoutLifecycleOverlay()
    {
        var empty = new MvCatchUpResult(0, false) { Outcome = MvCatchUpOutcome.Empty };
        var unsafeWindow = new MvCatchUpResult(0, true) { Outcome = MvCatchUpOutcome.UnsafeWindow };
        var retryable = new MvCatchUpResult(0, false)
        {
            Outcome = MvCatchUpOutcome.RetryableFailure,
            ErrorCode = "catch-up-failed",
            IsRetryable = true
        };
        var permanent = new MvCatchUpResult(0, false)
        {
            Outcome = MvCatchUpOutcome.PermanentUnsupported,
            ErrorCode = "unsupported",
            IsRetryable = false
        };
        var failedRead = new MvCatchUpResult(0, false)
        {
            Outcome = MvCatchUpOutcome.FailedRead,
            ErrorCode = "event-read-failed",
            IsRetryable = true
        };

        Assert.False(empty.IsFailure);
        Assert.False(unsafeWindow.IsFailure);
        Assert.True(retryable.IsFailure);
        Assert.True(retryable.IsRetryable);
        Assert.True(permanent.IsFailure);
        Assert.False(permanent.IsRetryable);
        Assert.True(failedRead.IsFailure);
        Assert.True(failedRead.IsRetryable);
        Assert.Null(empty.ProjectionStatus);
        Assert.Null(retryable.ProjectionStatus);
        Assert.Null(permanent.ProjectionStatus);
        Assert.Null(failedRead.ProjectionStatus);
    }

    [Fact]
    public void FixedAgedDescendingSixtyFourPairModel_KillsGlobalCursorLossMutant()
    {
        var fixedTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var durableEvents = Enumerable.Range(0, 64)
            .SelectMany(pair => new[]
            {
                new ProofEvent(SortableUniqueId.Generate(fixedTimestamp.AddMilliseconds(pair * 2), Guid.Empty)),
                new ProofEvent(SortableUniqueId.Generate(fixedTimestamp.AddMilliseconds(pair * 2 + 1), Guid.Empty))
            })
            .OrderBy(item => item.Position.Value, StringComparer.Ordinal)
            .ToArray();

        var storeDriven = new CursorProofModel();
        var oldGlobalCursorMutant = new CursorProofModel();
        foreach (var hint in durableEvents.Reverse())
        {
            storeDriven.ApplyOrderedStoreBatch(durableEvents);
            oldGlobalCursorMutant.ApplyInlineHint(hint);
        }

        Assert.Equal(128, storeDriven.AppliedCount);
        Assert.Equal(durableEvents[^1].Position.Value, storeDriven.CurrentPosition);
        Assert.Equal(1, oldGlobalCursorMutant.AppliedCount);
        Assert.NotEqual(storeDriven.AppliedCount, oldGlobalCursorMutant.AppliedCount);
    }

    [Fact]
    public void MaterializedViewGrain_EpochKeyFencesGenerationTargetTruthAndLifecycleStatus()
    {
        var target = MvCheckpointTruth.Known(
            new SortableUniqueId("062135600400000000000000000000"),
            MvCheckpointProvenance.AuthoritativeTargetCapture());
        var current = MvCheckpointTruth.Known(
            new SortableUniqueId("062135600300000000000000000000"),
            MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var entry = new MvRegistryEntry
        {
            ServiceId = "orders",
            ViewName = "WeatherForecast",
            ViewVersion = 1,
            LogicalTable = "forecasts",
            PhysicalTable = "weather_forecasts",
            Status = MvStatus.Active,
            CurrentCheckpointTruth = current,
            TargetCheckpointTruth = target
        };
        var active = new MvActiveEntry("orders", "WeatherForecast", 1, DateTimeOffset.UnixEpoch)
        {
            Generation = 7
        };

        var baseline = MaterializedViewGrain.CreateEpochKey([entry], active);
        var generationChanged = MaterializedViewGrain.CreateEpochKey(
            [entry],
            active with { Generation = 8 });
        var targetChanged = MaterializedViewGrain.CreateEpochKey(
            [entry with { TargetCheckpointTruth = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture()) }],
            active);
        var statusChanged = MaterializedViewGrain.CreateEpochKey(
            [entry with { Status = MvStatus.Ready }],
            active);

        Assert.NotEqual(baseline, generationChanged);
        Assert.NotEqual(baseline, targetChanged);
        Assert.NotEqual(baseline, statusChanged);
    }

    [Fact]
    public void MaterializedViewGrainStatus_KeepsPositionalAbiAndAddsHaltedAsInitProperty()
    {
        var constructor = typeof(MaterializedViewGrainStatus).GetConstructors().Single();
        Assert.Equal(18, constructor.GetParameters().Length);

        var deconstruct = typeof(MaterializedViewGrainStatus).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "Deconstruct");
        Assert.Equal(18, deconstruct.GetParameters().Length);

        var status = new MaterializedViewGrainStatus(
            "orders",
            "WeatherForecast",
            1,
            Started: true,
            CatchUpInProgress: false,
            SubscriptionActive: true,
            BufferedEventCount: 0,
            CurrentPosition: null,
            LastReceivedSortableUniqueId: null,
            LastError: null,
            LastCatchUpStartedAt: null,
            LastCatchUpCompletedAt: null)
        {
            CatchUpHalted = true
        };

        Assert.True(status.CatchUpHalted);
        var haltedProperty = typeof(MaterializedViewGrainStatus).GetProperty(nameof(MaterializedViewGrainStatus.CatchUpHalted))!;
        Assert.Contains(
            haltedProperty.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "IdAttribute" &&
                         attribute.ConstructorArguments.Count == 1 &&
                         Convert.ToUInt32(attribute.ConstructorArguments[0].Value) == 18U);
    }

    [Fact]
    public void MvExecutorBase_PreservesLegacyProtectedCatchUpHelperOverloads()
    {
        var methods = typeof(MvExecutorBase<>).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        Assert.Contains(methods, method => method.Name == "CatchUpFromStoreAsync" && method.GetParameters().Length == 5);
        Assert.Contains(methods, method => method.Name == "CatchUpFromStoreAsync" && method.GetParameters().Length == 7);
        Assert.Contains(methods, method => method.Name == "CompleteCatchUpAsync" && method.GetParameters().Length == 5);
        Assert.Contains(methods, method => method.Name == "CompleteCatchUpAsync" && method.GetParameters().Length == 6);
    }

    private sealed record ProofEvent(SortableUniqueId Position);

    private sealed class CursorProofModel
    {
        private readonly HashSet<string> _applied = new(StringComparer.Ordinal);

        public int AppliedCount => _applied.Count;
        public string? CurrentPosition { get; private set; }

        public void ApplyOrderedStoreBatch(IEnumerable<ProofEvent> durableEvents)
        {
            foreach (var item in durableEvents
                         .Where(item => CurrentPosition is null ||
                                        string.Compare(item.Position.Value, CurrentPosition, StringComparison.Ordinal) > 0)
                         .OrderBy(item => item.Position.Value, StringComparer.Ordinal))
            {
                _applied.Add(item.Position.Value);
                CurrentPosition = item.Position.Value;
            }
        }

        public void ApplyInlineHint(ProofEvent hint)
        {
            if (CurrentPosition is null || string.Compare(hint.Position.Value, CurrentPosition, StringComparison.Ordinal) > 0)
            {
                _applied.Add(hint.Position.Value);
                CurrentPosition = hint.Position.Value;
            }
        }
    }
}
