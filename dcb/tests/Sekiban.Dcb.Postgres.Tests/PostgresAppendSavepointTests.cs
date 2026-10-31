using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class PostgresAppendSavepointTests(PostgresTestFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task ExpectedPositionAppend_SkipsSavepoints_WhileOrdinaryAppendKeepsThem()
    {
        var baselineCounter = new SavepointCounter();
        var baselineStore = Store(baselineCounter);
        var baseline = await baselineStore.WriteSerializableEventsAsync([EventAt("0100", "Order:baseline")]);
        Assert.True(baseline.IsSuccess, baseline.IsSuccess ? "" : baseline.GetException().ToString());
        Assert.True(baselineCounter.Count > 0, "The ordinary append must prove the interceptor observes savepoints.");

        await using (var context = await Fixture.GetDbContextAsync())
        {
            context.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch
            {
                ServiceId = DefaultServiceIdProvider.DefaultServiceId,
                EnabledAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var expectedCounter = new SavepointCounter();
        string[] tags = ["Order:expected-a", "Order:expected-b", "Order:expected-c"];
        var specification = new ExpectedTagPositionSpecification(tags.Select(tag =>
            new TagHeadExpectationEntry(DefaultServiceIdProvider.DefaultServiceId, tag, TagHeadExpectation.AssertEmpty()))
            .ToArray());
        var expected = await Store(expectedCounter).WriteSerializableEventsWithExpectedTagPositionsAsync(
            [EventAt("0200", tags)], specification);
        Assert.True(expected.IsSuccess, expected.IsSuccess ? "" : expected.GetException().ToString());
        Assert.Equal(0, expectedCounter.Count);
    }

    private PostgresEventStore Store(SavepointCounter counter) =>
        new(new InterceptingDbContextFactory(Fixture.ConnectionString, counter), Fixture.DomainTypes.EventTypes,
            new FixedServiceIdProvider(DefaultServiceIdProvider.DefaultServiceId));

    private static SerializableEvent EventAt(string position, params string[] tags) => new(
        "{}"u8.ToArray(), position, Guid.CreateVersion7(), new EventMetadata("cause", "correlation", "savepoint-test"),
        tags.ToList(), "SavepointMarker");

    private sealed class InterceptingDbContextFactory(string connectionString, IInterceptor interceptor)
        : IDbContextFactory<SekibanDcbDbContext>
    {
        public SekibanDcbDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<SekibanDcbDbContext>()
                .UseNpgsql(connectionString).AddInterceptors(interceptor).Options);
    }

    private sealed class SavepointCounter : DbTransactionInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult CreatingSavepoint(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        public override ValueTask<InterceptionResult> CreatingSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
