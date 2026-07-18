using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Factory for creating ServiceId-scoped CosmosDbEventStore instances.
/// </summary>
public sealed class CosmosDbEventStoreFactory : IEventStoreFactory, IStorageDurabilityDescriptorProvider,
    IWriteConditionCapabilityProvider
{
    /// <summary>Every store this factory builds writes to the same durable backend.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "CosmosDb (per-service factory)");

    /// <summary>Every store this factory builds is a conditional (single-event unique-key) store.</summary>
    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        WriteConditionCapabilityDescriptor.Supporting(
            "CosmosDb (per-service factory)", WriteConditionKind.SingleEventUniqueKey);

    private readonly CosmosDbContext _context;
    private readonly IEventTypes _eventTypes;
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    ///     Creates a CosmosDb event store factory.
    /// </summary>
    public CosmosDbEventStoreFactory(
        CosmosDbContext context,
        IEventTypes eventTypes,
        ICosmosContainerResolver containerResolver,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<CosmosDbEventStore>();
        return new CosmosDbEventStore(_context, _eventTypes, provider, _containerResolver, logger);
    }
}
