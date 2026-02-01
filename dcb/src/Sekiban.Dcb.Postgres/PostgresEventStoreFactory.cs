using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Factory for creating ServiceId-scoped PostgresEventStore instances.
/// </summary>
public sealed class PostgresEventStoreFactory : IEventStoreFactory
{
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly DcbDomainTypes _domainTypes;
    private readonly ILoggerFactory? _loggerFactory;

    public PostgresEventStoreFactory(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        DcbDomainTypes domainTypes,
        ILoggerFactory? loggerFactory = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _loggerFactory = loggerFactory;
    }

    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<PostgresEventStore>();
        return new PostgresEventStore(_contextFactory, _domainTypes, provider, logger);
    }
}
