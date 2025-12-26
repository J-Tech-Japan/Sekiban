using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Background service to initialize CosmosDB containers on startup
/// </summary>
public class CosmosDbInitializer : IHostedService
{
    private readonly CosmosDbContext _context;
    private readonly ILogger<CosmosDbInitializer>? _logger;

    public CosmosDbInitializer(CosmosDbContext context, ILogger<CosmosDbInitializer>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogInformation("Initializing CosmosDB containers...");

            // This will trigger the initialization of containers if they don't exist
            await _context.GetEventsContainerAsync().ConfigureAwait(false);
            await _context.GetTagsContainerAsync().ConfigureAwait(false);

            _logger?.LogInformation("CosmosDB containers initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize CosmosDB containers");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}