using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Sqlite;

/// <summary>Creates SQLite event stores bound to one exact ServiceId over one shared database file.</summary>
public sealed class SqliteEventStoreFactory : IEventStoreFactory, IStorageDurabilityDescriptorProvider,
    IWriteConditionCapabilityProvider
{
    private readonly string _databasePath;
    private readonly IEventTypes _eventTypes;
    private readonly SqliteEventStoreOptions _options;
    private readonly ILoggerFactory? _loggerFactory;

    public SqliteEventStoreFactory(
        string databasePath,
        IEventTypes eventTypes,
        SqliteEventStoreOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _options = options ?? new SqliteEventStoreOptions();
        _loggerFactory = loggerFactory;
    }

    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "SQLite (per-service factory)");

    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        WriteConditionCapabilityDescriptor.Supporting(
            "SQLite (per-service factory)", WriteConditionKind.SingleEventUniqueKey);

    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<SqliteEventStore>();
        return new SqliteEventStore(_databasePath, _eventTypes, _options, logger, provider);
    }
}
