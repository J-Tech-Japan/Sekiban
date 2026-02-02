using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ServiceId;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Background service to initialize CosmosDB containers on startup
/// </summary>
public class CosmosDbInitializer : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogInitializingContainers =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(LogInitializingContainers)), "Initializing CosmosDB containers...");

    private static readonly Action<ILogger, Exception?> LogContainersInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(2, nameof(LogContainersInitialized)), "CosmosDB containers initialized successfully");

    private static readonly Action<ILogger, Exception?> LogInitializationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(3, nameof(LogInitializationFailed)), "Failed to initialize CosmosDB containers");

    private readonly CosmosDbContext _context;
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly ILogger<CosmosDbInitializer>? _logger;

    /// <summary>
    ///     Creates a new CosmosDB initializer hosted service.
    /// </summary>
    public CosmosDbInitializer(
        CosmosDbContext context,
        ICosmosContainerResolver containerResolver,
        ILogger<CosmosDbInitializer>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _logger = logger;
    }

    /// <summary>
    ///     Initializes CosmosDB containers at startup.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_logger != null)
            {
                LogInitializingContainers(_logger, null);
            }

            // This will trigger the initialization of containers if they don't exist
            var defaultServiceId = DefaultServiceIdProvider.DefaultServiceId;
            var eventsSettings = _containerResolver.ResolveEventsContainer(defaultServiceId);
            var tagsSettings = _containerResolver.ResolveTagsContainer(defaultServiceId);
            var statesSettings = _containerResolver.ResolveStatesContainer(defaultServiceId);

            await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
            await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);
            await _context.GetMultiProjectionStatesContainerAsync(statesSettings).ConfigureAwait(false);

            if (_logger != null)
            {
                LogContainersInitialized(_logger, null);
            }
        }
        catch (Exception ex)
        {
            if (_logger != null)
            {
                LogInitializationFailed(_logger, ex);
            }
            throw;
        }
    }

    /// <summary>
    ///     Stops the hosted service (no-op).
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
