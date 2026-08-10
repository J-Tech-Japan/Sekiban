using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.InMemory;

#pragma warning disable CS0618

/// <summary>
/// Creates service-bound in-memory event stores over one shared process-local backend.
/// This is intended for tests and development only.
/// </summary>
public sealed class InMemoryEventStoreFactory : IEventStoreFactory, IStorageDurabilityDescriptorProvider
{
    private readonly IEventTypes _eventTypes;
    private readonly InMemoryEventStoreBackend _backend;

    public InMemoryEventStoreFactory(IEventTypes eventTypes)
        : this(eventTypes, new InMemoryEventStoreBackend())
    {
    }

    /// <summary>Creates a factory sharing the backend already owned by the supplied store.</summary>
    public InMemoryEventStoreFactory(InMemoryEventStore sharedStore)
        : this(sharedStore?.EventTypes ?? throw new ArgumentNullException(nameof(sharedStore)), sharedStore.Backend)
    {
    }

    private InMemoryEventStoreFactory(IEventTypes eventTypes, InMemoryEventStoreBackend backend)
    {
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Volatile, "InMemory (shared per-service factory)");

    public IEventStore CreateForService(string serviceId) =>
        new InMemoryEventStore(_eventTypes, new FixedServiceIdProvider(serviceId), _backend);
}

#pragma warning restore CS0618
