using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>Creates DynamoDB event stores bound to one exact ServiceId over one shared table backend.</summary>
public sealed class DynamoDbEventStoreFactory : IEventStoreFactory, IStorageDurabilityDescriptorProvider,
    IWriteConditionCapabilityProvider
{
    private readonly DynamoDbContext _context;
    private readonly IEventTypes _eventTypes;
    private readonly ILoggerFactory? _loggerFactory;

    public DynamoDbEventStoreFactory(
        DynamoDbContext context,
        IEventTypes eventTypes,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _loggerFactory = loggerFactory;
    }

    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "DynamoDB (per-service factory)");

    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        WriteConditionCapabilityDescriptor.Supporting(
            "DynamoDB (per-service factory)", WriteConditionKind.SingleEventUniqueKey);

    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<DynamoDbEventStore>();
        return new DynamoDbEventStore(_context, _eventTypes, provider, logger);
    }
}
