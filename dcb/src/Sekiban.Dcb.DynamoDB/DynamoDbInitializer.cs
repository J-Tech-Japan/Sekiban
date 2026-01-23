using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.DynamoDB;

#pragma warning disable CA1031

/// <summary>
///     Background service to initialize DynamoDB tables on startup.
/// </summary>
public class DynamoDbInitializer : IHostedService
{
    private static readonly Action<ILogger, Exception?> LogInitializingTables =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(LogInitializingTables)),
            "Initializing DynamoDB tables...");

    private static readonly Action<ILogger, Exception?> LogTablesInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(2, nameof(LogTablesInitialized)),
            "DynamoDB tables initialized successfully");

    private static readonly Action<ILogger, Exception?> LogInitializationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(3, nameof(LogInitializationFailed)),
            "Failed to initialize DynamoDB tables");

    private readonly DynamoDbContext _context;
    private readonly ILogger<DynamoDbInitializer>? _logger;

    /// <summary>
    ///     Initializes a new DynamoDbInitializer.
    /// </summary>
    public DynamoDbInitializer(DynamoDbContext context, ILogger<DynamoDbInitializer>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    /// <summary>
    ///     Starts the table initialization process.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_logger != null)
                LogInitializingTables(_logger, null);

            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            if (_logger != null)
                LogTablesInitialized(_logger, null);
        }
        catch (Exception ex)
        {
            if (_logger != null)
                LogInitializationFailed(_logger, ex);
            throw;
        }
    }

    /// <summary>
    ///     Stops the initializer (no-op).
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
