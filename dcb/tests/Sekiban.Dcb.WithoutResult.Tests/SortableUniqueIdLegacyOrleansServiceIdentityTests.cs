using System.Reflection;
using Dcb.Domain.WithoutResult;
using Orleans;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport;

namespace Sekiban.Dcb.WithoutResult.Tests;

public class SortableUniqueIdLegacyOrleansServiceIdentityTests
{
    [Fact]
    public async Task RetainedSixArgumentFacadeSeedsAmbientServiceAThenBSeparately()
    {
        var domain = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<IdentityProbeEvent>();
        var suffix = Guid.NewGuid().ToString("N");
        var serviceA = $"g31-orleans-noresult-a-{suffix}";
        var serviceB = $"g31-orleans-noresult-b-{suffix}";
        var service = new G31MutableServiceIdProvider(serviceA);
        var inner = new InMemoryEventStore(domain.EventTypes, service);
        await SeedAsync(inner, domain, service, serviceA, 2035);
        await SeedAsync(inner, domain, service, serviceB, 2040);
        var store = new G31ServiceHeadCountingEventStore(inner, service);
        var client = DispatchProxy.Create<IClusterClient, ThrowingClusterClientProxy>();
        var executor = new OrleansDcbExecutor(client, store, domain, null, service, null);

        service.Current = serviceA;
        await executor.ExecuteAsync(
            new IdentityProbeCommand(),
            (_, _) => Task.FromResult(EventOrNone.FromValue(new EventPayloadWithTags(new IdentityProbeEvent("a"), []))));
        service.Current = serviceB;
        await executor.ExecuteAsync(
            new IdentityProbeCommand(),
            (_, _) => Task.FromResult(EventOrNone.FromValue(new EventPayloadWithTags(new IdentityProbeEvent("b"), []))));

        Assert.Equal(1, store.HeadReadsFor(serviceA));
        Assert.Equal(1, store.HeadReadsFor(serviceB));
    }

    private static async Task SeedAsync(
        InMemoryEventStore store,
        DcbDomainTypes domain,
        G31MutableServiceIdProvider service,
        string serviceId,
        int year)
    {
        service.Current = serviceId;
        var persisted = new Event(
                new IdentityProbeEvent("seed"),
                SortableUniqueId.Generate(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid()),
                nameof(IdentityProbeEvent),
                Guid.CreateVersion7(),
                new EventMetadata("seed", "seed", "test"),
                [])
            .ToSerializableEvent(domain.EventTypes);
        Assert.True((await store.WriteSerializableEventsAsync([persisted])).IsSuccess);
    }

    private sealed record IdentityProbeCommand : ICommand;
    private sealed record IdentityProbeEvent(string Value) : IEventPayload;

    private class ThrowingClusterClientProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected Orleans client call: {targetMethod?.Name}");
    }
}
