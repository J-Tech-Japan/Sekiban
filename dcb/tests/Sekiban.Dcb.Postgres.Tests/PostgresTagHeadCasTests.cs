using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using ResultBoxes;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Real-PostgreSQL acceptance evidence for SEK-G40's durable expected-tag-position protocol. These tests inspect
///     provisioned rows through fresh DbContexts; they never replace the production head collection/locking protocol with
///     a test-owned dictionary or lock.
/// </summary>
public sealed class PostgresTagHeadCasTests : PostgresTestBase
{
    private const string DefaultService = DefaultServiceIdProvider.DefaultServiceId;

    public PostgresTagHeadCasTests(PostgresTestFixture fixture) : base(fixture) { }

    private PostgresEventStore Store(string serviceId = DefaultService) =>
        new(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new FixedServiceIdProvider(serviceId));

    private static SerializableEvent EventAt(string position, params string[] tags) => new(
        "{}"u8.ToArray(),
        position,
        Guid.CreateVersion7(),
        new EventMetadata("cause", "correlation", "g40-test"),
        tags.ToList(),
        "G40Marker");

    private static ExpectedTagPositionSpecification Spec(
        string serviceId,
        params (string Tag, TagHeadExpectation Expected)[] entries) =>
        new(entries.Select(entry => new TagHeadExpectationEntry(serviceId, entry.Tag, entry.Expected)).ToArray());

    private async Task EnableEpochAsync(string serviceId = DefaultService)
    {
        await using var context = await Fixture.GetDbContextAsync();
        context.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch
        {
            ServiceId = serviceId,
            EnabledAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private async Task<DbTagHead?> HeadAsync(string tag, string serviceId = DefaultService)
    {
        await using var context = await Fixture.GetDbContextAsync();
        return await context.TagHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ServiceId == serviceId && row.Tag == tag);
    }

    [Fact]
    public async Task ThreeStates_EpochGate_NoEnforcementAndExactAdvanceTheRealDurableHead()
    {
        const string tag = "Order:three-state";
        var store = Store();

        // NoEnforcement is still a full head-protocol write even before the operator turns on enforcement.
        var noEnforcement = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0001", tag)],
            Spec(DefaultService, (tag, TagHeadExpectation.NoEnforcement())));
        Assert.True(noEnforcement.IsSuccess, noEnforcement.IsSuccess ? "" : noEnforcement.GetException().ToString());
        Assert.Equal("0001", (await HeadAsync(tag))!.HeadPosition);

        // Exact is a durable fence and must fail before its provider write / head advance while the epoch is unset.
        var beforeEpoch = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0002", tag)],
            Spec(DefaultService, (tag, TagHeadExpectation.Exact("0001"))));
        Assert.False(beforeEpoch.IsSuccess);
        Assert.IsType<TagHeadEnforcementNotEnabledException>(beforeEpoch.GetException());
        Assert.Equal("0001", (await HeadAsync(tag))!.HeadPosition);
        await using (var inspection = await Fixture.GetDbContextAsync())
        {
            Assert.Single(await inspection.Events.AsNoTracking().ToListAsync());
        }

        await EnableEpochAsync();
        var exact = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0002", tag)],
            Spec(DefaultService, (tag, TagHeadExpectation.Exact("0001"))));
        Assert.True(exact.IsSuccess, exact.IsSuccess ? "" : exact.GetException().ToString());
        Assert.Equal("0002", (await HeadAsync(tag))!.HeadPosition);
    }

    [Fact]
    public async Task VersionedSerializedV2_RealExecutorAndPostgresPath_UsesTheSameExpectedHeadProtocol()
    {
        var studentId = Guid.CreateVersion7();
        var tag = new StudentTag(studentId).GetTag();
        await EnableEpochAsync();
        var store = Store();
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, Fixture.DomainTypes),
            Fixture.DomainTypes);
        var payload = Fixture.DomainTypes.EventTypes.SerializeEventPayload(new StudentCreated(studentId, "v2"));
        var request = new VersionedExpectedTagPositionSerializedCommitRequest(
            VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
            [new SerializableEventCandidate(System.Text.Encoding.UTF8.GetBytes(payload), nameof(StudentCreated), [tag])],
            [new ConsistencyTagEntry(tag, string.Empty)],
            [new TagHeadExpectationEntry(DefaultService, tag, TagHeadExpectation.AssertEmpty())]);

        var result = await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(request);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var written = Assert.Single(result.GetValue().WrittenEvents);
        Assert.Equal(written.SortableUniqueIdValue, (await HeadAsync(tag))!.HeadPosition);
    }

    [Fact]
    public async Task WithResultCommandOptions_ActualPostgresPath_DerivesTheConsistencyTagAndUsesAssertEmptyThenExact()
    {
        var studentId = Guid.CreateVersion7();
        var tag = new StudentTag(studentId).GetTag();
        await EnableEpochAsync();
        var store = Store();
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, Fixture.DomainTypes),
            Fixture.DomainTypes);

        var first = await executor.ExecuteAsync(
            new TagHeadTestCommand(),
            (_, context) => context.AppendEvent(new StudentCreated(studentId, "ordinary-v2"), new StudentTag(studentId)),
            new CommandExecutionOptions
            {
                ExpectedTagPositions = Spec(DefaultService, (tag, TagHeadExpectation.AssertEmpty()))
            });
        Assert.True(first.IsSuccess, first.IsSuccess ? "" : first.GetException().ToString());
        var firstPosition = first.GetValue().SortableUniqueId
            ?? throw new InvalidOperationException("Successful command execution did not return a sortable position.");
        Assert.Equal(firstPosition, (await HeadAsync(tag))!.HeadPosition);

        var second = await executor.ExecuteAsync(
            new TagHeadTestCommand(),
            (_, context) => context.AppendEvent(new StudentCreated(studentId, "ordinary-v2-next"), new StudentTag(studentId)),
            new CommandExecutionOptions
            {
                ExpectedTagPositions = Spec(DefaultService, (tag, TagHeadExpectation.Exact(firstPosition)))
            });
        Assert.True(second.IsSuccess, second.IsSuccess ? "" : second.GetException().ToString());
        Assert.Equal(second.GetValue().SortableUniqueId, (await HeadAsync(tag))!.HeadPosition);
    }

    [Fact]
    public async Task DerivedExpectationEntries_MissingDuplicateUnknownMalformedAndServiceMismatch_FailBeforeStoreMutation()
    {
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        var firstTag = new StudentTag(firstId).GetTag();
        var secondTag = new StudentTag(secondId).GetTag();
        var store = Store();
        var executor = new GeneralSekibanExecutor(
            store,
            new InMemoryObjectAccessor(store, Fixture.DomainTypes),
            Fixture.DomainTypes);

        async Task<(ResultBox<ExecutionResult> Result, bool HandlerInvoked)> ExecuteInvalidAsync(
            ExpectedTagPositionSpecification specification)
        {
            var handlerInvoked = false;
            var result = await executor.ExecuteAsync(
                new TagHeadTestCommand(),
                (_, context) =>
                {
                    handlerInvoked = true;
                    return context.AppendEvent(
                        new StudentCreated(firstId, "invalid-spec"),
                        new StudentTag(firstId),
                        new StudentTag(secondId));
                },
                new CommandExecutionOptions { ExpectedTagPositions = specification });
            return (result, handlerInvoked);
        }

        var missing = await ExecuteInvalidAsync(Spec(DefaultService, (firstTag, TagHeadExpectation.NoEnforcement())));
        Assert.False(missing.Result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(missing.Result.GetException());
        Assert.True(missing.HandlerInvoked); // exact derived-tag validation occurs after the handler, still before a write

        var duplicate = await ExecuteInvalidAsync(Spec(DefaultService,
            (firstTag, TagHeadExpectation.NoEnforcement()),
            (firstTag, TagHeadExpectation.NoEnforcement()),
            (secondTag, TagHeadExpectation.NoEnforcement())));
        Assert.False(duplicate.Result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(duplicate.Result.GetException());
        Assert.False(duplicate.HandlerInvoked);

        var unknown = await ExecuteInvalidAsync(Spec(DefaultService,
            (firstTag, TagHeadExpectation.NoEnforcement()),
            (secondTag, TagHeadExpectation.NoEnforcement()),
            ("Student:unknown", TagHeadExpectation.NoEnforcement())));
        Assert.False(unknown.Result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(unknown.Result.GetException());
        Assert.True(unknown.HandlerInvoked);

        var malformed = await ExecuteInvalidAsync(Spec(DefaultService,
            (firstTag, new TagHeadExpectation(TagHeadExpectationKind.Exact)),
            (secondTag, TagHeadExpectation.NoEnforcement())));
        Assert.False(malformed.Result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(malformed.Result.GetException());
        Assert.False(malformed.HandlerInvoked);

        var wrongService = await ExecuteInvalidAsync(new ExpectedTagPositionSpecification(
            [
                new TagHeadExpectationEntry("other-service", firstTag, TagHeadExpectation.NoEnforcement()),
                new TagHeadExpectationEntry(DefaultService, secondTag, TagHeadExpectation.NoEnforcement())
            ]));
        Assert.False(wrongService.Result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(wrongService.Result.GetException());
        Assert.False(wrongService.HandlerInvoked);

        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Empty(await inspection.Events.AsNoTracking().ToListAsync());
        Assert.Empty(await inspection.Tags.AsNoTracking().ToListAsync());
        Assert.Empty(await inspection.TagHeads.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_ExistingAuthoritativeTagHistoryIsNotMistakenForAssertEmpty()
    {
        const string historicalTag = "Order:historical";
        const string emptyTag = "Order:proven-empty";
        var store = Store();

        // Simulate years of pre-epoch dcb_tags history: remove its protocol row after a legacy writer populated dcb_tags.
        Assert.True((await store.WriteSerializableEventsAsync([EventAt("0010", historicalTag)])).IsSuccess);
        await using (var prepare = await Fixture.GetDbContextAsync())
        {
            await prepare.Database.ExecuteSqlRawAsync(
                "DELETE FROM dcb_tag_heads WHERE \"ServiceId\" = {0} AND \"Tag\" = {1}", DefaultService, historicalTag);
        }
        await EnableEpochAsync();

        var staleEmpty = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0020", historicalTag)],
            Spec(DefaultService, (historicalTag, TagHeadExpectation.AssertEmpty())));
        Assert.False(staleEmpty.IsSuccess);
        var conflict = Assert.IsType<ExpectedTagPositionConflictException>(staleEmpty.GetException());
        var pair = Assert.Single(conflict.Pairs);
        Assert.Equal("0010", pair.ObservedPosition); // authoritative dcb_tags MAX, not absent=head-empty
        Assert.Null(await HeadAsync(historicalTag)); // ordinary mismatch rolls back its lazy bootstrap row too

        var provenEmpty = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0030", emptyTag)],
            Spec(DefaultService, (emptyTag, TagHeadExpectation.AssertEmpty())));
        Assert.True(provenEmpty.IsSuccess, provenEmpty.IsSuccess ? "" : provenEmpty.GetException().ToString());
        Assert.Equal("0030", (await HeadAsync(emptyTag))!.HeadPosition);
    }

    [Fact]
    public async Task Reconciliation_IsServiceScoped_AndCombinedRepairMismatchLeavesOnlyRepairDurable()
    {
        const string a = "Order:A";
        const string b = "Order:B";
        const string c = "Order:C-lazy";
        var store = Store();
        await EnableEpochAsync();

        await using (var setup = await Fixture.GetDbContextAsync())
        {
            setup.TagHeads.AddRange(
                new DbTagHead { ServiceId = DefaultService, Tag = a, HeadPosition = "0010" },
                new DbTagHead { ServiceId = DefaultService, Tag = b, HeadPosition = "0020" });
            // A old/non-participating writer bypassed the head. It is reconciliation evidence, not current command DML.
            setup.Tags.Add(DbTag.FromEventTag(a, "Order", "0015", Guid.CreateVersion7(), "Bypass", DefaultService));
            // Cross-service same textual tag has a much larger position and must not repair/default-service-audit anything.
            setup.Tags.Add(DbTag.FromEventTag(a, "Order", "9999", Guid.CreateVersion7(), "OtherService", "other"));
            await setup.SaveChangesAsync();
        }

        var outcome = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0030", a, b, c)],
            Spec(DefaultService,
                (a, TagHeadExpectation.Exact("0010")),
                (b, TagHeadExpectation.Exact("wrong")),
                (c, TagHeadExpectation.NoEnforcement())));

        Assert.False(outcome.IsSuccess);
        var conflict = Assert.IsType<ExpectedTagPositionConflictException>(outcome.GetException());
        Assert.Equal(new[] { a, b, c }, conflict.Pairs.Select(pair => pair.Tag).ToArray()); // complete combined set
        Assert.Equal("0015", conflict.Pairs.Single(pair => pair.Tag == a).ObservedPosition);
        Assert.Equal("0020", conflict.Pairs.Single(pair => pair.Tag == b).ObservedPosition);
        Assert.Null(conflict.Pairs.Single(pair => pair.Tag == c).ObservedPosition);

        // Fresh-connection inspection: repair/audit A commits; B is unchanged; lazy C and all current command rows vanish.
        Assert.Equal("0015", (await HeadAsync(a))!.HeadPosition);
        Assert.Equal("0020", (await HeadAsync(b))!.HeadPosition);
        Assert.Null(await HeadAsync(c));
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Equal(2, await inspection.Tags.AsNoTracking().CountAsync()); // setup A + cross-service control, no command tag
        Assert.Empty(await inspection.Events.AsNoTracking().ToListAsync());
        var violation = Assert.Single(await inspection.TagHeadViolations.AsNoTracking().ToListAsync());
        Assert.Equal(DefaultService, violation.ServiceId);
        Assert.Equal(a, violation.Tag);
        Assert.Equal("0010", violation.PreviousHeadPosition);
        Assert.Equal("0015", violation.ObservedPosition);
    }

    [Fact]
    public async Task CrossServiceSameTagHigherPosition_IsNeitherViolationNorRepair()
    {
        const string service = "alpha";
        const string tag = "Order:shared";
        await EnableEpochAsync(service);
        await using (var setup = await Fixture.GetDbContextAsync())
        {
            setup.TagHeads.Add(new DbTagHead { ServiceId = service, Tag = tag, HeadPosition = "0010" });
            setup.Tags.Add(DbTag.FromEventTag(tag, "Order", "9999", Guid.CreateVersion7(), "Other", "beta"));
            await setup.SaveChangesAsync();
        }

        var result = await Store(service).WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0020", tag)],
            Spec(service, (tag, TagHeadExpectation.Exact("0010"))));
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal("0020", (await HeadAsync(tag, service))!.HeadPosition);
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Empty(await inspection.TagHeadViolations.AsNoTracking().Where(v => v.ServiceId == service).ToListAsync());
    }

    [Theory]
    [InlineData("Order:reconcile-below-batch", "0200")]
    [InlineData("Order:reconcile-inside-batch", "0350")]
    [InlineData("Order:reconcile-above-batch", "0500")]
    public async Task Reconciliation_DetectsAuthoritativeBypassMaximaAcrossTheWholeBatchRange(
        string tag,
        string bypassMaximum)
    {
        await EnableEpochAsync();
        await using (var setup = await Fixture.GetDbContextAsync())
        {
            setup.TagHeads.Add(new DbTagHead { ServiceId = DefaultService, Tag = tag, HeadPosition = "0100" });
            setup.Tags.Add(DbTag.FromEventTag(
                tag, "Order", bypassMaximum, Guid.CreateVersion7(), "OldWriter", DefaultService));
            await setup.SaveChangesAsync();
        }

        var result = await Store().WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0300", tag), EventAt("0400", tag)],
            Spec(DefaultService, (tag, TagHeadExpectation.Exact("0100"))));

        Assert.False(result.IsSuccess);
        var conflict = Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
        var pair = Assert.Single(conflict.Pairs);
        Assert.Equal("0100", pair.Expected.Position);
        Assert.Equal(bypassMaximum, pair.ObservedPosition);

        // Reconciliation's repair/audit is intentionally durable even though both candidate command rows are absent.
        Assert.Equal(bypassMaximum, (await HeadAsync(tag))!.HeadPosition);
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Empty(await inspection.Events.AsNoTracking().ToListAsync());
        Assert.Single(await inspection.Tags.AsNoTracking().ToListAsync()); // only the authoritative bypass evidence
        var violation = Assert.Single(await inspection.TagHeadViolations.AsNoTracking().ToListAsync());
        Assert.Equal("0100", violation.PreviousHeadPosition);
        Assert.Equal(bypassMaximum, violation.ObservedPosition);
    }

    [Fact]
    public async Task ReconciliationAudit_RollsBackOnFailure_ThenRemainsIdempotentAndAppendOnlyOnRetry()
    {
        const string tag = "Order:audit-atomic";
        await EnableEpochAsync();
        await using (var setup = await Fixture.GetDbContextAsync())
        {
            setup.TagHeads.Add(new DbTagHead { ServiceId = DefaultService, Tag = tag, HeadPosition = "0100" });
            setup.Tags.Add(DbTag.FromEventTag(tag, "Order", "0200", Guid.CreateVersion7(), "OldWriter", DefaultService));
            await setup.SaveChangesAsync();
        }

        var logger = new CountingLogger();
        var store = new PostgresEventStore(
            Fixture.DbContextFactory,
            Fixture.DomainTypes.EventTypes,
            new FixedServiceIdProvider(DefaultService),
            logger);
        var calls = 0;
        store.TagHeadProtocolHook = () => ++calls == 3
            ? Task.FromException(new InvalidOperationException("abort after reconciliation"))
            : Task.CompletedTask;
        try
        {
            var failed = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("0300", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("0100"))));
            Assert.False(failed.IsSuccess);
        }
        finally
        {
            store.TagHeadProtocolHook = null;
        }

        await using (var aborted = await Fixture.GetDbContextAsync())
        {
            Assert.Equal("0100", (await aborted.TagHeads.AsNoTracking()
                .SingleAsync(row => row.ServiceId == DefaultService && row.Tag == tag)).HeadPosition);
            Assert.Empty(await aborted.TagHeadViolations.AsNoTracking().ToListAsync());
            Assert.Empty(await aborted.Events.AsNoTracking().ToListAsync());
        }
        Assert.Equal(0, logger.WarningCount); // a rolled-back audit must never produce a false committed-violation log

        var repaired = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0300", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("0100"))));
        Assert.False(repaired.IsSuccess);
        Assert.IsType<ExpectedTagPositionConflictException>(repaired.GetException());
        Assert.Equal("0200", (await HeadAsync(tag))!.HeadPosition);
        Assert.Equal(1, logger.WarningCount); // emitted only after the repair/audit transaction committed

        // The same stale retry now has no new M to reconcile: it cannot create a second record and must not clean up the
        // append-only first one.
        var retry = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0300", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("0100"))));
        Assert.False(retry.IsSuccess);
        await using var committed = await Fixture.GetDbContextAsync();
        Assert.Single(await committed.TagHeadViolations.AsNoTracking().ToListAsync());
        Assert.Empty(await committed.Events.AsNoTracking().ToListAsync());
        Assert.Equal(1, logger.WarningCount); // no duplicate log without a new durable reconciliation record
    }

    [Fact]
    public async Task Reconciliation_NoEnforcementAndLegacyWritersStillCommitAndUseMaximumOfRepairAndBatch()
    {
        const string noEnforcementTag = "Order:reconcile-no-enforcement";
        const string legacyTag = "Order:reconcile-legacy";
        await using (var setup = await Fixture.GetDbContextAsync())
        {
            setup.TagHeads.AddRange(
                new DbTagHead { ServiceId = DefaultService, Tag = noEnforcementTag, HeadPosition = "0100" },
                new DbTagHead { ServiceId = DefaultService, Tag = legacyTag, HeadPosition = "0100" });
            setup.Tags.AddRange(
                DbTag.FromEventTag(noEnforcementTag, "Order", "0150", Guid.CreateVersion7(), "OldWriter", DefaultService),
                DbTag.FromEventTag(legacyTag, "Order", "0500", Guid.CreateVersion7(), "OldWriter", DefaultService));
            await setup.SaveChangesAsync();
        }

        // NoEnforcement skips comparison only: it sees the repair first, commits its event, then advances from 0150 to
        // the per-tag batch maximum 0200 without needing the enforcement epoch.
        var noEnforcement = await Store().WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0200", noEnforcementTag)],
            Spec(DefaultService, (noEnforcementTag, TagHeadExpectation.NoEnforcement())));
        Assert.True(noEnforcement.IsSuccess, noEnforcement.IsSuccess ? "" : noEnforcement.GetException().ToString());

        // An unconditional legacy write likewise commits. Its older batch position may not regress the repaired 0500
        // head; its event remains durable, but the head is max(repaired head, batch maximum).
        var legacy = await Store().WriteSerializableEventsAsync([EventAt("0300", legacyTag)]);
        Assert.True(legacy.IsSuccess, legacy.IsSuccess ? "" : legacy.GetException().ToString());

        Assert.Equal("0200", (await HeadAsync(noEnforcementTag))!.HeadPosition);
        Assert.Equal("0500", (await HeadAsync(legacyTag))!.HeadPosition);
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Equal(2, await inspection.Events.AsNoTracking().CountAsync());
        Assert.Equal(4, await inspection.Tags.AsNoTracking().CountAsync()); // two bypass rows + two command rows
        Assert.Equal(2, await inspection.TagHeadViolations.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task PartialStateFailures_AfterEachProductionStage_RollBackFromFreshConnection()
    {
        foreach (var (callNumber, stage) in new[]
                 {
                     (4, "after event insertion"),
                     (5, "after tag-index insertion"),
                     (6, "after head update before commit")
                 })
        {
            await Fixture.ClearDatabaseAsync();
            var store = Store();
            var calls = 0;
            store.TagHeadProtocolHook = () => ++calls == callNumber
                ? Task.FromException(new InvalidOperationException($"inject-{stage}"))
                : Task.CompletedTask;
            try
            {
                var failed = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
                    [EventAt("0100", "Order:partial")],
                    Spec(DefaultService, ("Order:partial", TagHeadExpectation.NoEnforcement())));
                Assert.False(failed.IsSuccess);
            }
            finally
            {
                store.TagHeadProtocolHook = null;
            }

            await using var inspection = await Fixture.GetDbContextAsync();
            Assert.Empty(await inspection.Events.AsNoTracking().ToListAsync());
            Assert.Empty(await inspection.Tags.AsNoTracking().ToListAsync());
            Assert.Empty(await inspection.TagHeads.AsNoTracking().ToListAsync());
            Assert.Empty(await inspection.TagHeadViolations.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task RealPostgresRaces_AssertEmptyAndNonemptyHeadCommitExactlyOne()
    {
        const string emptyTag = "Order:race-empty";
        await EnableEpochAsync();
        var emptyFirst = Store();
        var emptySecond = Store();
        var emptyResults = await RunOverlappedAtCanonicalInsertionAsync(
            emptyFirst,
            emptySecond,
            () => emptyFirst.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("0200", emptyTag)], Spec(DefaultService, (emptyTag, TagHeadExpectation.AssertEmpty()))),
            () => emptySecond.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("0201", emptyTag)], Spec(DefaultService, (emptyTag, TagHeadExpectation.AssertEmpty()))));
        Assert.Single(emptyResults, result => result.IsSuccess);
        Assert.Single(emptyResults, result => !result.IsSuccess && result.GetException() is ExpectedTagPositionConflictException);
        Assert.Equal(1, await CountEventsAsync());
        await using (var emptyInspection = await Fixture.GetDbContextAsync())
        {
            Assert.Single(await emptyInspection.TagHeads.AsNoTracking().ToListAsync()); // no duplicate lazy head row
        }

        await Fixture.ClearDatabaseAsync();
        var tag = "Order:race-nonempty";
        var bootstrap = Store();
        Assert.True((await bootstrap.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0300", tag)], Spec(DefaultService, (tag, TagHeadExpectation.NoEnforcement())))).IsSuccess);
        await EnableEpochAsync();
        var nonemptyFirst = Store();
        var nonemptySecond = Store();
        var nonemptyResults = await RunOverlappedAtCanonicalInsertionAsync(
            nonemptyFirst,
            nonemptySecond,
            () => nonemptyFirst.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("0301", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("0300")))),
            () => nonemptySecond.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("0302", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("0300")))));
        Assert.Single(nonemptyResults, result => result.IsSuccess);
        Assert.Single(nonemptyResults, result => !result.IsSuccess && result.GetException() is ExpectedTagPositionConflictException);
        Assert.Equal(2, await CountEventsAsync()); // bootstrap + exactly one current command, never merely at-most-one
    }

    [Fact]
    public async Task EnforcedAndUnconditionalRace_EveryCommittedTaggedWriteRemainsVisibleInTheDurableHead()
    {
        const string tag = "Order:enforced-vs-legacy";
        var bootstrap = Store();
        Assert.True((await bootstrap.WriteSerializableEventsAsync([EventAt("1000", tag)])).IsSuccess);
        await EnableEpochAsync();

        var fenced = Store();
        var legacy = Store();
        var arrivals = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> overlap = async () =>
        {
            if (Interlocked.Increment(ref arrivals) > 2)
            {
                return;
            }
            if (Volatile.Read(ref arrivals) == 2)
            {
                release.TrySetResult();
            }
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        fenced.TagHeadProtocolHook = overlap;
        legacy.TagHeadProtocolHook = overlap;
        try
        {
            var fencedTask = fenced.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("3000", tag)], Spec(DefaultService, (tag, TagHeadExpectation.Exact("1000"))));
            var legacyTask = legacy.WriteSerializableEventsAsync([EventAt("2000", tag)]);
            await Task.WhenAll(fencedTask, legacyTask).WaitAsync(TimeSpan.FromSeconds(15));
            var fencedResult = await fencedTask;
            var legacyResult = await legacyTask;

            Assert.True(legacyResult.IsSuccess, legacyResult.IsSuccess ? "" : legacyResult.GetException().ToString());
            if (fencedResult.IsSuccess)
            {
                // Even if the older-position legacy writer commits second, it cannot regress 3000 back to 2000.
                Assert.Equal("3000", (await HeadAsync(tag))!.HeadPosition);
                Assert.Equal(3, await CountEventsAsync());
            }
            else
            {
                Assert.IsType<ExpectedTagPositionConflictException>(fencedResult.GetException());
                Assert.Equal("2000", (await HeadAsync(tag))!.HeadPosition);
                Assert.Equal(2, await CountEventsAsync());
            }
        }
        finally
        {
            fenced.TagHeadProtocolHook = null;
            legacy.TagHeadProtocolHook = null;
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task CanonicalInsertionAndLockOrder_ReversedCallerOrders_NoSqlState40P01()
    {
        const string a = "Order:a";
        const string z = "Order:z";
        var first = Store();
        var second = Store();
        var arrived = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> overlap = async () =>
        {
            // The first hook invocation for each writer occurs immediately before canonical lazy insertion. Later
            // milestones must not re-enter the barrier.
            if (Interlocked.Increment(ref arrived) > 2)
            {
                return;
            }
            if (Volatile.Read(ref arrived) == 2)
            {
                release.TrySetResult();
            }
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        first.TagHeadProtocolHook = overlap;
        second.TagHeadProtocolHook = overlap;
        try
        {
            var results = await Task.WhenAll(
                    first.WriteSerializableEventsAsync([EventAt("0400", z, a)]),
                    second.WriteSerializableEventsAsync([EventAt("0401", a, z)]))
                .WaitAsync(TimeSpan.FromSeconds(15));
            Assert.DoesNotContain(results.Where(result => !result.IsSuccess).Select(result => result.GetException()), ContainsDeadlock);
            Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString()));
        }
        finally
        {
            first.TagHeadProtocolHook = null;
            second.TagHeadProtocolHook = null;
        }
    }

    [Fact]
    public async Task StrictPositions_UsePerTagMaximum_NotLastGlobalPosition_AndRejectBeforeRows()
    {
        const string a = "Order:max-a";
        const string b = "Order:max-b";
        var store = Store();
        var valid = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0500", a), EventAt("0600", b), EventAt("0700", a)],
            Spec(DefaultService,
                (a, TagHeadExpectation.NoEnforcement()),
                (b, TagHeadExpectation.NoEnforcement())));
        Assert.True(valid.IsSuccess, valid.IsSuccess ? "" : valid.GetException().ToString());
        Assert.Equal("0700", (await HeadAsync(a))!.HeadPosition);
        Assert.Equal("0600", (await HeadAsync(b))!.HeadPosition); // exact per-tag maximum, not global final 0700

        // Continue from both exact heads in a second multi-tag batch. This is not merely a first-write check: every
        // resulting durable head is pinned after the protocol has already established non-empty state.
        await EnableEpochAsync();
        var continuation = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0800", b), EventAt("0900", a)],
            Spec(DefaultService,
                (a, TagHeadExpectation.Exact("0700")),
                (b, TagHeadExpectation.Exact("0600"))));
        Assert.True(continuation.IsSuccess, continuation.IsSuccess ? "" : continuation.GetException().ToString());
        Assert.Equal("0900", (await HeadAsync(a))!.HeadPosition);
        Assert.Equal("0800", (await HeadAsync(b))!.HeadPosition);

        var invalid = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0900", a), EventAt("0800", b)],
            Spec(DefaultService,
                (a, TagHeadExpectation.NoEnforcement()),
                (b, TagHeadExpectation.NoEnforcement())));
        Assert.False(invalid.IsSuccess);
        Assert.IsType<TagHeadPositionValidationException>(invalid.GetException());

        // Batch order itself is valid here, but 0650 is behind the existing A head. This separately proves the
        // per-event/per-tag floor rather than merely the global batch-order check above.
        var behindHead = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0650", a), EventAt("0800", b)],
            Spec(DefaultService,
                (a, TagHeadExpectation.NoEnforcement()),
                (b, TagHeadExpectation.NoEnforcement())));
        Assert.False(behindHead.IsSuccess);
        Assert.IsType<TagHeadPositionValidationException>(behindHead.GetException());
        Assert.Equal(5, await CountEventsAsync()); // neither invalid command left DML or lazy state behind
    }

    [Fact]
    public async Task BestEffortBoundary_AfterReconciliationOldWriterIsNotPretendedToBeAudited()
    {
        const string tag = "Order:best-effort";
        var store = Store();
        Assert.True((await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("1000", tag)], Spec(DefaultService, (tag, TagHeadExpectation.NoEnforcement())))).IsSuccess);
        await EnableEpochAsync();

        var reconciliationReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var protocolCalls = 0;
        store.TagHeadProtocolHook = async () =>
        {
            // Invocation 3 is immediately after the production reconciliation MAX query and any repair DML.
            if (Interlocked.Increment(ref protocolCalls) == 3)
            {
                reconciliationReached.TrySetResult();
                await allowCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        };
        try
        {
            var newWriter = store.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [EventAt("3000", tag)], Spec(DefaultService, (tag, TagHeadExpectation.NoEnforcement())));
            await reconciliationReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // This stands for an incorrectly undrained pre-epoch writer. It runs AFTER the authoritative MAX query, so
            // the test documents the honest boundary rather than falsely claiming the current reconciliation saw it.
            await using (var oldWriter = await Fixture.GetDbContextAsync())
            {
                oldWriter.Tags.Add(DbTag.FromEventTag(tag, "Order", "2000", Guid.CreateVersion7(), "OldWriter", DefaultService));
                await oldWriter.SaveChangesAsync();
            }
            allowCommit.TrySetResult();
            var result = await newWriter;
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }
        finally
        {
            store.TagHeadProtocolHook = null;
            allowCommit.TrySetResult();
        }

        Assert.Equal("3000", (await HeadAsync(tag))!.HeadPosition);
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Empty(await inspection.TagHeadViolations.AsNoTracking().ToListAsync());
        Assert.Contains(await inspection.Tags.AsNoTracking().Select(row => row.SortableUniqueId).ToListAsync(), p => p == "2000");
    }

    [Fact]
    public async Task TypedSerializedAndConditionalWriters_AllAdvanceTheSameHeadTable()
    {
        const string typedTag = "Order:typed";
        const string serializedTag = "Order:serialized";
        const string claimTag = "Order:conditional";
        var store = Store();

        // Typed route reaches the common seam via real EventStoreExtensions serialization.
        var typed = new Event(
            new StudentCreated(Guid.CreateVersion7(), "writer"),
            "0800",
            nameof(StudentCreated),
            Guid.CreateVersion7(),
            new EventMetadata("c", "r", "u"),
            [typedTag]);
        Assert.True((await store.WriteEventsAsync([typed])).IsSuccess);
        Assert.True((await store.WriteSerializableEventsAsync([EventAt("0810", serializedTag)])).IsSuccess);
        var conditionalEvent = new Event(
                new StudentCreated(Guid.CreateVersion7(), "conditional"),
                "0820",
                nameof(StudentCreated),
                Guid.CreateVersion7(),
                new EventMetadata("c", "r", "u"),
                [claimTag])
            .ToSerializableEvent(Fixture.DomainTypes.EventTypes);
        var claim = await ((IConditionalEventStore)store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("g40-common-seam", conditionalEvent));
        Assert.True(claim.IsSuccess, claim.IsSuccess ? "" : claim.GetException().ToString());

        Assert.Equal("0800", (await HeadAsync(typedTag))!.HeadPosition);
        Assert.Equal("0810", (await HeadAsync(serializedTag))!.HeadPosition);
        Assert.Equal("0820", (await HeadAsync(claimTag))!.HeadPosition);
    }

    private static async Task<T[]> RunOverlappedAtCanonicalInsertionAsync<T>(
        PostgresEventStore first,
        PostgresEventStore second,
        Func<Task<T>> firstWrite,
        Func<Task<T>> secondWrite)
    {
        var arrivals = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<Task> overlap = async () =>
        {
            // Each store's first callback is immediately before production lazy insertion. Synchronizing only those first
            // two callbacks forces two independent Postgres transactions to contend at the canonical insertion/lock seam.
            if (Interlocked.Increment(ref arrivals) > 2)
            {
                return;
            }
            if (Volatile.Read(ref arrivals) == 2)
            {
                release.TrySetResult();
            }
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        first.TagHeadProtocolHook = overlap;
        second.TagHeadProtocolHook = overlap;
        try
        {
            return await Task.WhenAll(firstWrite(), secondWrite()).WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            first.TagHeadProtocolHook = null;
            second.TagHeadProtocolHook = null;
            release.TrySetResult();
        }
    }

    private async Task<int> CountEventsAsync()
    {
        await using var context = await Fixture.GetDbContextAsync();
        return await context.Events.AsNoTracking().CountAsync();
    }

    private static bool ContainsDeadlock(Exception exception) => exception switch
    {
        PostgresException { SqlState: "40P01" } => true,
        _ when exception.InnerException is not null => ContainsDeadlock(exception.InnerException),
        _ => false
    };

    private sealed record TagHeadTestCommand : ICommand;

    private sealed class CountingLogger : ILogger<PostgresEventStore>
    {
        private int _warningCount;
        public int WarningCount => Volatile.Read(ref _warningCount);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warningCount);
            }
        }
    }
}
