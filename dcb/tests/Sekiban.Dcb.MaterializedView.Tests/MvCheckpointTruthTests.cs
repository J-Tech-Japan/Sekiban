using System.Text.Json;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MaterializedView;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvCheckpointTruthTests
{
    private static readonly SortableUniqueId NonZeroPosition =
        new(SortableUniqueId.Generate(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc), Guid.Empty));

    [Fact]
    public void Unknown_KnownZero_AndKnownNonzero_RoundTripWithDistinctWireTruth()
    {
        var unknown = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.NotObserved);
        var knownZero = MvCheckpointTruth.KnownZero(
            new MvCheckpointProvenance(
                MvCheckpointProvenanceKind.AuthoritativeEmptyHistory,
                DateTimeOffset.Parse("2026-08-10T12:00:00Z")));
        var known = MvCheckpointTruth.Known(
            NonZeroPosition,
            new MvCheckpointProvenance(
                MvCheckpointProvenanceKind.AppliedEvent,
                DateTimeOffset.Parse("2026-08-10T12:01:00Z"),
                MvApplySource.CatchUp));

        var unknownWire = MvCheckpointTruthCodec.Encode(unknown);
        var knownZeroWire = MvCheckpointTruthCodec.Encode(knownZero);
        var knownWire = MvCheckpointTruthCodec.Encode(known);

        Assert.NotEqual(unknownWire, knownZeroWire);
        Assert.NotEqual(knownZeroWire, knownWire);
        Assert.True(MvCheckpointTruthCodec.Decode(unknownWire).IsUnknown);
        Assert.Equal(MvCheckpointUnknownReason.NotObserved, MvCheckpointTruthCodec.Decode(unknownWire).UnknownReason);
        Assert.True(MvCheckpointTruthCodec.Decode(knownZeroWire).IsKnownZero);
        Assert.Equal(SortableUniqueId.MinValue.Value, MvCheckpointTruthCodec.Decode(knownZeroWire).PositionValue);
        Assert.Equal(NonZeroPosition.Value, MvCheckpointTruthCodec.Decode(knownWire).PositionValue);
        Assert.Equal(MvCheckpointProvenanceKind.AppliedEvent, MvCheckpointTruthCodec.Decode(knownWire).Provenance!.Kind);
    }

    [Fact]
    public void Unknown_NeverSatisfiesKnownZeroOrKnownPosition()
    {
        var unknown = MvCheckpointTruth.Unknown();
        var knownZero = MvCheckpointTruth.KnownZero();
        var known = MvCheckpointTruth.Known(NonZeroPosition, MvCheckpointProvenance.AppliedEvent(MvApplySource.Stream));

        Assert.False(unknown.Satisfies(knownZero));
        Assert.False(unknown.Satisfies(known));
        Assert.False(unknown.Satisfies(SortableUniqueId.MinValue.Value));
        Assert.True(known.Satisfies(knownZero));
        Assert.True(known.Satisfies(NonZeroPosition.Value));
        Assert.False(knownZero.Satisfies(NonZeroPosition.Value));
    }

    [Fact]
    public void LegacyNull_DecodesAsUnknown_WithoutFabricatingZero()
    {
        var decoded = MvCheckpointTruthCodec.Decode(null);

        Assert.True(decoded.IsUnknown);
        Assert.Equal(MvCheckpointUnknownReason.LegacyNull, decoded.UnknownReason);
        Assert.Null(decoded.PositionValue);
        Assert.False(decoded.IsKnownZero);
    }

    [Fact]
    public void PublicRegistryEntry_RoundTripsCheckpointTruthThroughJson()
    {
        var entry = new MvRegistryEntry
        {
            ServiceId = "service",
            ViewName = "View",
            ViewVersion = 1,
            LogicalTable = "rows",
            PhysicalTable = "view_rows",
            CurrentCheckpointTruth = MvCheckpointTruth.Known(
                NonZeroPosition,
                MvCheckpointProvenance.AppliedEvent(MvApplySource.Stream)),
            TargetCheckpointTruth = MvCheckpointTruth.KnownZero(),
            LastUpdated = DateTimeOffset.UtcNow
        };

        var roundTripped = JsonSerializer.Deserialize<MvRegistryEntry>(JsonSerializer.Serialize(entry));

        Assert.NotNull(roundTripped);
        Assert.Equal(NonZeroPosition.Value, roundTripped!.CurrentCheckpointTruth.PositionValue);
        Assert.True(roundTripped.TargetCheckpointTruth.IsKnownZero);
    }

    [Theory]
    [InlineData("{\"state\":\"known\",\"position\":\"bad\",\"knownZero\":false,\"provenance\":{\"kind\":\"appliedEvent\",\"observedAtUtc\":\"2026-08-10T12:00:00Z\"}}")]
    [InlineData("{\"state\":\"known\",\"position\":\"000000000000000000000000000000\",\"knownZero\":false,\"provenance\":{\"kind\":\"appliedEvent\",\"observedAtUtc\":\"2026-08-10T12:00:00Z\"}}")]
    [InlineData("{\"state\":\"known\",\"position\":\"000000000000000000000000000000\",\"knownZero\":true}")]
    [InlineData("{\"state\":\"maybe\"}")]
    public void MalformedTruth_FailsWithTypedException(string serialized)
    {
        var exception = Assert.Throws<MvCheckpointMalformedException>(() => MvCheckpointTruthCodec.Decode(serialized));

        Assert.False(string.IsNullOrWhiteSpace(exception.FieldName));
    }
}
