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
    TimeProvider timeProvider,
    RetainedColdSegmentHolders holders) : IEventStoreFactory, IStorageDurabilityDescriptorProvider, IWriteConditionCapabilityProvider
{
    public IEventStore CreateForService(string serviceId)
    {
        var serviceIdProvider = new FixedServiceIdProvider(serviceId);
        return new HybridEventStore(
            inner.CreateForService(serviceId), coldStorage, formatHandler, serviceIdProvider,
            options, logger, holders.Get(serviceIdProvider.GetCurrentServiceId()), timeProvider);
    }

    public StorageDurabilityDescriptor DescribeStorage() =>
        SekibanDcbCapabilityResolver.DescribeStorage(inner, "hot event store");

    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        SekibanDcbCapabilityResolver.DescribeWriteConditions(inner, "hot event store");
}
