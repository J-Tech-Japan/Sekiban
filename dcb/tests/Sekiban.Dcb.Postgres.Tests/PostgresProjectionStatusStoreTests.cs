using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Real PostgreSQL proof for SEK-G35. The statement must reach its conflict/update arm for an existing row while
///     still rejecting an expected&gt;0 write when the row is absent; a simplified unconditional INSERT is not acceptable.
/// </summary>
[Collection("PostgresTests")]
public sealed class PostgresProjectionStatusStoreTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;

    public PostgresProjectionStatusStoreTests(PostgresTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS dcb_projection_statuses");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CasMatrix_UsesReachableUpdateAndConditionalCreateAgainstRealPostgres()
    {
        var store = new PostgresMultiProjectionStateStore(
            _fixture.DbContextFactory,
            new FixedServiceIdProvider("svc"));
        var first = Heartbeat("activation-a", 1);

        var created = await store.UpsertAsync(first, 0);
        var updated = await store.UpsertAsync(first with { Sequence = 2, AppliedEventCount = 2 }, 1);
        var staleCreate = await store.UpsertAsync(first with { Sequence = 3 }, 0);

        Assert.True(created.IsSuccess);
        Assert.True(created.GetValue().Committed);
        Assert.True(updated.IsSuccess);
        Assert.True(updated.GetValue().Committed);
        Assert.Equal(2, updated.GetValue().Current!.Sequence);
        Assert.Equal(2, updated.GetValue().Current!.AppliedEventCount);
        Assert.True(staleCreate.IsSuccess);
        var createRaceConflict = Assert.IsType<ProjectionStatusWriteConflict>(staleCreate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAlreadyExists, createRaceConflict.Reason);
        Assert.Equal(0, createRaceConflict.ExpectedSequence);
        Assert.Equal(2, createRaceConflict.ObservedSequence);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ExpectedProjectorVersion);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ObservedProjectorVersion);
        Assert.Equal(createRaceConflict.ToCompatibilityReason(), staleCreate.GetValue().ConflictReason);
        Assert.DoesNotContain("activation row already exists", staleCreate.GetValue().ConflictReason!, StringComparison.OrdinalIgnoreCase);

        var staleUpdate = await store.UpsertAsync(first with { Sequence = 3 }, 1);
        Assert.True(staleUpdate.IsSuccess);
        var staleUpdateConflict = Assert.IsType<ProjectionStatusWriteConflict>(staleUpdate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.SequenceMismatch, staleUpdateConflict.Reason);
        Assert.Equal(1, staleUpdateConflict.ExpectedSequence);
        Assert.Equal(2, staleUpdateConflict.ObservedSequence);

        var missing = first with { ClusterId = "missing", Sequence = 2 };
        var absent = await store.UpsertAsync(missing, 1);
        Assert.True(absent.IsSuccess);
        var absentConflict = Assert.IsType<ProjectionStatusWriteConflict>(absent.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAbsent, absentConflict.Reason);
        Assert.Equal(1, absentConflict.ExpectedSequence);
        Assert.Null(absent.GetValue().Current);
        Assert.Null(absentConflict.ObservedSequence);
        Assert.Equal(missing.ProjectorVersion, absentConflict.ExpectedProjectorVersion);
        Assert.Null(absentConflict.ObservedProjectorVersion);
        var listedAfterAbsentWrite = await store.ListAsync();
        Assert.True(
            listedAfterAbsentWrite.IsSuccess,
            listedAfterAbsentWrite.IsSuccess ? string.Empty : listedAfterAbsentWrite.GetException().ToString());
        Assert.DoesNotContain(listedAfterAbsentWrite.GetValue(), row => row.ClusterId == "missing");

        // A later conditional create can race. The loser observes RowAlreadyExists and must not overwrite the winner.
        var competitor = missing with { ActivationId = "competitor", Sequence = 1 };
        Assert.True((await store.UpsertAsync(competitor, 0)).GetValue().Committed);
        var losingCreate = await store.UpsertAsync(missing with { ActivationId = "retrying", Sequence = 1 }, 0);
        Assert.True(losingCreate.IsSuccess);
        Assert.Equal(
            ProjectionStatusConflictReason.RowAlreadyExists,
            Assert.IsType<ProjectionStatusWriteConflict>(losingCreate.GetValue().ConflictDetails).Reason);
        Assert.Equal("competitor", losingCreate.GetValue().Current!.ActivationId);

        var afterRebase = await store.UpsertAsync(missing with { ActivationId = "retrying", Sequence = 2 }, 1);
        Assert.True(afterRebase.IsSuccess);
        Assert.True(afterRebase.GetValue().Committed);
        Assert.Equal(2, afterRebase.GetValue().Current!.Sequence);
    }

    private static ProjectionStatusHeartbeat Heartbeat(string activationId, long sequence) =>
        new(
            "svc",
            "status-projector",
            "v1",
            "cluster-a",
            activationId,
            sequence,
            sequence,
            null,
            null,
            DateTimeOffset.UtcNow)
        {
            Phase = ProjectionStatusPhases.Active
        };
}
