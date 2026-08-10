using Microsoft.Data.Sqlite;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvActivationEligibilityTests
{
    [Fact]
    public void UnknownTarget_IsRejectedBeforeAnyActivationRequestIsCreated()
    {
        var current = KnownCheckpoint("current", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var entry = CreateEntry(
            "orders",
            "Weather",
            2,
            MvStatus.Ready,
            current,
            MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable));

        var (eligibility, request) = MvActivationEligibility.Evaluate(
            "orders",
            "Weather",
            2,
            [entry],
            active: null);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, eligibility.FailureReason);
        Assert.Null(request);
    }

    [Fact]
    public void BehindCandidate_IsRejectedEvenWhenASeparateBestEffortStatusWouldSayCaughtUp()
    {
        // This value represents the kind of sampled G24 status that must not be consulted by G27. The eligibility
        // API has no status/count input; only the persisted authoritative truth below can authorize a cutover.
        var g24IsCaughtUp = true;
        var current = KnownCheckpoint("current", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("target", MvCheckpointProvenance.AuthoritativeTargetCapture());
        var entry = CreateEntry("orders", "Weather", 2, MvStatus.Ready, current, target);

        var (eligibility, request) = MvActivationEligibility.Evaluate(
            "orders",
            "Weather",
            2,
            [entry],
            active: null);

        Assert.True(g24IsCaughtUp);
        Assert.False(eligibility.IsEligible);
        Assert.Equal(MvActivationFailureReason.BehindTarget, eligibility.FailureReason);
        Assert.Null(request);
    }

    [Fact]
    public void EligibleCandidate_CarriesExpectedActiveAndGenerationSnapshot()
    {
        var checkpoint = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("same", MvCheckpointProvenance.AuthoritativeTargetCapture());
        var entry = CreateEntry("orders", "Weather", 2, MvStatus.Ready, checkpoint, target);
        var active = new MvActiveEntry("orders", "Weather", 1, DateTimeOffset.UtcNow)
        {
            Generation = 7
        };

        var (eligibility, request) = MvActivationEligibility.Evaluate(
            "orders",
            "Weather",
            2,
            [entry],
            active);

        Assert.True(eligibility.IsEligible);
        Assert.NotNull(request);
        Assert.Equal(1, request.ExpectedActiveVersion);
        Assert.Equal(7, request.ExpectedActiveGeneration);
        Assert.Equal(MvStatus.Ready, request.ExpectedStatus);
    }

    [Fact]
    public async Task SqliteRegistry_UsesExpectedGenerationAndLeavesPointerUnchangedForInvalidOrStaleRequests()
    {
        var connectionString = $"Data Source=file:sek-g27-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        var store = new SqliteMvRegistryStore(connectionString);
        await store.EnsureInfrastructureAsync();

        var checkpoint = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("same", MvCheckpointProvenance.AuthoritativeTargetCapture());
        await store.RegisterAsync(CreateEntry("orders", "Weather", 2, MvStatus.Ready, checkpoint, target));

        var entries = await store.GetEntriesAsync("orders", "Weather", 2);
        var (eligibility, request) = MvActivationEligibility.Evaluate("orders", "Weather", 2, entries, null);
        Assert.True(eligibility.IsEligible);
        Assert.NotNull(request);

        var invalid = await store.TryActivateAsync(
            request! with
            {
                ExpectedTargetCheckpointTruth = MvCheckpointTruthCodec.Encode(
                    MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.Malformed))
            });
        Assert.False(invalid.Succeeded);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, invalid.FailureReason);
        Assert.Null(await store.GetActiveAsync("orders", "Weather"));

        var winner = await store.TryActivateAsync(request!);
        var stale = await store.TryActivateAsync(request!);

        Assert.True(winner.Succeeded);
        Assert.Equal(1, winner.NewGeneration);
        Assert.False(stale.Succeeded);
        Assert.True(stale.IsConflict);

        var active = await store.GetActiveAsync("orders", "Weather");
        Assert.NotNull(active);
        Assert.Equal(2, active.ActiveVersion);
        Assert.Equal(1, active.Generation);
        Assert.All(
            await store.GetEntriesAsync("orders", "Weather", 2),
            entry => Assert.Equal(MvStatus.Active, entry.Status));
    }

    private static MvRegistryEntry CreateEntry(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        MvCheckpointTruth current,
        MvCheckpointTruth target) =>
        new()
        {
            ServiceId = serviceId,
            ViewName = viewName,
            ViewVersion = viewVersion,
            LogicalTable = "main",
            PhysicalTable = "weather_main",
            Status = status,
            CurrentPosition = current.PositionValue,
            TargetPosition = target.PositionValue,
            CurrentCheckpointTruth = current,
            TargetCheckpointTruth = target,
            AppliedEventVersion = 1,
            LastUpdated = DateTimeOffset.UtcNow
        };

    private static MvCheckpointTruth KnownCheckpoint(
        string seed,
        MvCheckpointProvenance provenance)
    {
        var id = GuidUtility.Create(seed);
        return MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(DateTime.UnixEpoch.AddMinutes(1), id)),
            provenance);
    }
}

internal static class GuidUtility
{
    public static Guid Create(string value) =>
        new(value switch
        {
            "current" => "00000000-0000-0000-0000-000000000001",
            "target" => "00000000-0000-0000-0000-000000000002",
            "same" => "00000000-0000-0000-0000-000000000003",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
}
