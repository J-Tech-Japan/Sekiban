using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Order;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

#pragma warning disable CS0618

public sealed class MvServiceScopeTests
{
    [Fact]
    public async Task InMemoryFactory_SharesBackend_WhilePartitioningServices()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var seedStore = new InMemoryEventStore(domainTypes.EventTypes);
        var factory = new InMemoryEventStoreFactory(seedStore);
        var orders = factory.CreateForService("orders");
        var billing = factory.CreateForService("billing");
        var ordersAgain = factory.CreateForService("orders");

        var serializableEvent = new Event(
                new OrderCreated(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    DateTimeOffset.UtcNow),
                "100",
                nameof(OrderCreated),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                new EventMetadata("test", "test", "test"),
                [])
            .ToSerializableEvent(domainTypes.EventTypes);

        var writeResult = await orders.WriteSerializableEventsAsync([serializableEvent]);

        Assert.True(writeResult.IsSuccess);
        Assert.Single((await orders.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Single((await ordersAgain.ReadAllSerializableEventsAsync()).GetValue());
        Assert.Empty((await billing.ReadAllSerializableEventsAsync()).GetValue());
    }

}

#pragma warning restore CS0618
