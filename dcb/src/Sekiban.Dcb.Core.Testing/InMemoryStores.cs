using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Testing;

// These derive from the types that used to be the only place to get them, in Sekiban.Dcb.Core's Sekiban.Dcb.InMemory
// namespace. Those are now [Obsolete] — deliberately, and only to point here. Deriving from them is therefore
// expected, not an accident, and the warning would be noise.
#pragma warning disable CS0618 // Type or member is obsolete

/// <summary>
///     An event store that keeps everything in a dictionary in this process, and loses all of it when the process
///     ends. Correct for a unit test. Data loss anywhere else.
///     It declares itself <c>Volatile</c>, so a Production host that resolves it and has opted into
///     <c>AddSekibanDcbProductionGuard</c> will not start.
/// </summary>
public class InMemoryEventStore : Sekiban.Dcb.InMemory.InMemoryEventStore
{
    /// <summary>
    ///     Creates the store with reflection-discovered event types. Prefer the overload that takes the event types
    ///     your domain actually registered: reflection finds a different set, and a store that serialises against a
    ///     different set than the domain uses is a subtle way to make a test lie.
    /// </summary>
    public InMemoryEventStore(IServiceIdProvider? serviceIdProvider = null) : base(serviceIdProvider)
    {
    }

    /// <summary>Creates the store with the event types the domain registered. This is the one you want.</summary>
    public InMemoryEventStore(IEventTypes eventTypes, IServiceIdProvider? serviceIdProvider = null)
        : base(eventTypes, serviceIdProvider)
    {
    }
}

/// <summary>
///     Multi-projection state held in this process only. Rebuildable from events, but not itself durable — and it
///     says so, so the production guard can act on it.
/// </summary>
public class InMemoryMultiProjectionStateStore : Sekiban.Dcb.InMemory.InMemoryMultiProjectionStateStore
{
    /// <summary>Creates the store.</summary>
    public InMemoryMultiProjectionStateStore(IServiceIdProvider? serviceIdProvider = null) : base(serviceIdProvider)
    {
    }
}

/// <summary>
///     Actors as objects in a dictionary in this process: no cluster, no placement, no coordination with anything
///     else. It declares itself <c>TestingInProcess</c>.
/// </summary>
public class InMemoryObjectAccessor : Sekiban.Dcb.InMemory.InMemoryObjectAccessor
{
    /// <summary>Creates the accessor over a store and a domain.</summary>
    public InMemoryObjectAccessor(IEventStore eventStore, DcbDomainTypes domainTypes) : base(eventStore, domainTypes)
    {
    }
}

/// <summary>Publishes events to the in-process multi-projection actors held by an <see cref="InMemoryObjectAccessor" />.</summary>
public class InMemoryMultiProjectionEventPublisher : Sekiban.Dcb.InMemory.InMemoryMultiProjectionEventPublisher
{
    /// <summary>Creates the publisher over an in-process accessor.</summary>
    public InMemoryMultiProjectionEventPublisher(Sekiban.Dcb.InMemory.InMemoryObjectAccessor objectAccessor)
        : base(objectAccessor)
    {
    }
}

/// <summary>An event publisher that goes nowhere but this process.</summary>
public class InMemoryEventPublisher : Sekiban.Dcb.InMemory.InMemoryEventPublisher
{
    /// <summary>Creates the publisher.</summary>
    public InMemoryEventPublisher(IStreamDestinationResolver resolver) : base(resolver)
    {
    }
}

/// <summary>A stream that exists only in this process.</summary>
public class InMemorySekibanStream : Sekiban.Dcb.InMemory.InMemorySekibanStream
{
    /// <summary>Creates the stream.</summary>
    public InMemorySekibanStream(string topic = "events.all") : base(topic)
    {
    }
}

/// <summary>Resolves every destination to the one in-process stream.</summary>
public class InMemoryStreamDestinationResolver : Sekiban.Dcb.InMemory.InMemoryStreamDestinationResolver
{
    /// <summary>Creates the resolver.</summary>
    public InMemoryStreamDestinationResolver(ISekibanStream stream) : base(stream)
    {
    }
}

/// <summary>An event subscription served from this process.</summary>
public class InMemoryEventSubscription : Sekiban.Dcb.InMemory.InMemoryEventSubscription;

/// <summary>Snapshot payloads held in this process. Volatile, like everything else here.</summary>
public class InMemoryBlobStorageSnapshotAccessor : Sekiban.Dcb.Snapshots.InMemoryBlobStorageSnapshotAccessor;

#pragma warning restore CS0618
