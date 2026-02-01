using System.Text.Json;
using System.Linq;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Queries;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class PrimitiveMultiProjectionActorTests
{
    private static DcbDomainTypes CreateDomain()
    {
        return DcbDomainTypesExtensions.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<CounterIncremented>();
        });
    }

    private static Event MakeEvent(int amount)
    {
        return new Event(
            new CounterIncremented(amount),
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            typeof(CounterIncremented).FullName!,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "test"),
            new List<string> { "counter:main" });
    }

    [Fact]
    public async Task PrimitiveActor_Serializes_Only_On_Persist()
    {
        var domain = CreateDomain();
        var instance = new TestPrimitiveProjectionInstance(domain.JsonSerializerOptions);
        var host = new TestPrimitiveProjectionHost(instance);
        var actor = new PrimitiveMultiProjectionActor(domain, host, "primitive-counter");

        await actor.ApplyEventAsync(MakeEvent(1));
        await actor.ApplyEventAsync(MakeEvent(2));

        var queryResult = await actor.QueryAsync(new GetValueQuery());
        Assert.True(queryResult.IsSuccess);
        Assert.Equal(3, queryResult.GetValue());
        Assert.Equal(0, instance.SerializeCallCount);

        var snapshot = await actor.CreateSnapshotAsync();
        Assert.Equal(1, instance.SerializeCallCount);
        Assert.Equal("primitive-counter", snapshot.ProjectorName);
        Assert.Equal("3", snapshot.StateJson);
    }

    [Fact]
    public async Task PrimitiveActor_Restores_Snapshot_And_Queries()
    {
        var domain = CreateDomain();
        var instance = new TestPrimitiveProjectionInstance(domain.JsonSerializerOptions);
        var host = new TestPrimitiveProjectionHost(instance);
        var actor = new PrimitiveMultiProjectionActor(domain, host, "primitive-counter");

        await actor.ApplyEventAsync(MakeEvent(5));
        var snapshot = await actor.CreateSnapshotAsync();

        var restoredInstance = new TestPrimitiveProjectionInstance(domain.JsonSerializerOptions);
        var restoredHost = new TestPrimitiveProjectionHost(restoredInstance);
        var restoredActor = new PrimitiveMultiProjectionActor(domain, restoredHost, "primitive-counter");
        await restoredActor.RestoreSnapshotAsync(snapshot);

        var queryResult = await restoredActor.QueryAsync(new GetValueQuery());
        Assert.True(queryResult.IsSuccess);
        Assert.Equal(5, queryResult.GetValue());

        var listResult = await restoredActor.ListQueryAsync(new GetValuesQuery());
        Assert.True(listResult.IsSuccess);
        var list = listResult.GetValue();
        Assert.Single(list.Items);
        Assert.Equal(5, list.Items.First());
    }

    private sealed record CounterIncremented(int Amount) : IEventPayload;

    private sealed record GetValueQuery() : IQueryCommon<GetValueQuery, int>;

    private sealed record GetValuesQuery() : IListQueryCommon<GetValuesQuery, int>;

    private sealed class TestPrimitiveProjectionHost : IPrimitiveProjectionHost
    {
        private readonly IPrimitiveProjectionInstance _instance;

        public TestPrimitiveProjectionHost(IPrimitiveProjectionInstance instance)
        {
            _instance = instance;
        }

        public IPrimitiveProjectionInstance CreateInstance(string projectorName) => _instance;
    }

    private sealed class TestPrimitiveProjectionInstance : IPrimitiveProjectionInstance
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private int _value;

        public int SerializeCallCount { get; private set; }

        public TestPrimitiveProjectionInstance(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        public void ApplyEvent(string eventType, string eventPayloadJson, IReadOnlyList<string> tags, string? sortableUniqueId)
        {
            if (eventType != typeof(CounterIncremented).FullName)
            {
                return;
            }

            var payload = JsonSerializer.Deserialize<CounterIncremented>(eventPayloadJson, _jsonOptions);
            _value += payload?.Amount ?? 0;
        }

        public string ExecuteQuery(string queryType, string queryParamsJson)
        {
            return JsonSerializer.Serialize(_value, _jsonOptions);
        }

        public string ExecuteListQuery(string queryType, string queryParamsJson)
        {
            var result = new ListQueryResult<int>(1, 1, 1, 1, new[] { _value });
            return JsonSerializer.Serialize(result, _jsonOptions);
        }

        public string SerializeState()
        {
            SerializeCallCount++;
            return JsonSerializer.Serialize(_value, _jsonOptions);
        }

        public void RestoreState(string stateJson)
        {
            var restored = JsonSerializer.Deserialize<int>(stateJson, _jsonOptions);
            _value = restored;
        }

        public void Dispose()
        {
        }
    }
}
