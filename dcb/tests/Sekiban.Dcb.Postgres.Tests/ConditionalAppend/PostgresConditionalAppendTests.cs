using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Postgres conditional (unique-key) append against a real PostgreSQL container (Testcontainers). The shared
///     outcome-machine assertions (<see cref="ConditionalAppendScenarios" />) prove the uniform contract; the value this
///     provider adds is that its N-writer case is a genuine cross-transaction race converging on one durable winner via
///     the primary-key/23505 primitive.
/// </summary>
public class PostgresConditionalAppendTests : PostgresTestBase
{
    public PostgresConditionalAppendTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Register the marker types into the fixture's shared domain so the fingerprint can resolve them.
        ConditionalAppendScenarios.RegisterMarker(Fixture.DomainTypes);
    }

    private IConditionalEventStore Conditional => (IConditionalEventStore)Fixture.EventStore;

    private async Task<int> DurableCount() =>
        (await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue().Count();

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability((Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)Fixture.EventStore);

    [Fact]
    public Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent() =>
        ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            Conditional, Fixture.DomainTypes, "pg-1", DurableCount);

    [Fact]
    public Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause() =>
        ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            Conditional, Fixture.DomainTypes, "pg-2", DurableCount);

    [Fact]
    public Task NWriters_SameOperation_ConcurrentTransactions_OneAppended_RestAlreadyCommitted_OneDurableEvent() =>
        ConditionalAppendScenarios.AssertNWritersConverge(Conditional, Fixture.DomainTypes, "pg-race", 10, DurableCount);
}
