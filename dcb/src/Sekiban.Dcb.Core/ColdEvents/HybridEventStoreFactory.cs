using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.ColdEvents;

internal sealed class HybridEventStoreFactory(
    IEventStoreFactory inner,
    IColdObjectStorage coldStorage,
    IColdSegmentFormatHandler formatHandler,
    IOptions<ColdEventStoreOptions> options,
    ILogger<HybridEventStore> logger,
    TimeProvider timeProvider) : IEventStoreFactory, IStorageDurabilityDescriptorProvider, IWriteConditionCapabilityProvider
{
    private readonly ConcurrentDictionary<string, RetainedColdSegmentHolder> _holders = new(StringComparer.Ordinal);

    public IEventStore CreateForService(string serviceId) => new HybridEventStore(
        inner.CreateForService(serviceId), coldStorage, formatHandler, new FixedServiceIdProvider(serviceId),
        options, logger, _holders.GetOrAdd(serviceId, _ => new RetainedColdSegmentHolder()), timeProvider);

    public StorageDurabilityDescriptor DescribeStorage() =>
        SekibanDcbCapabilityResolver.DescribeStorage(inner, "hot event store");

    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        SekibanDcbCapabilityResolver.DescribeWriteConditions(inner, "hot event store");
}
