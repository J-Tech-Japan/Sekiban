using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Factory for creating ServiceId-scoped PostgresEventStore instances.
/// </summary>
public sealed class PostgresEventStoreFactory : IEventStoreFactory
{
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly IEventTypes _eventTypes;
    private readonly ILoggerFactory? _loggerFactory;

    public PostgresEventStoreFactory(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IEventTypes eventTypes,
        ILoggerFactory? loggerFactory = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _loggerFactory = loggerFactory;
    }

    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<PostgresEventStore>();
        return new PostgresEventStore(_contextFactory, _eventTypes, provider, logger);
    }
}
