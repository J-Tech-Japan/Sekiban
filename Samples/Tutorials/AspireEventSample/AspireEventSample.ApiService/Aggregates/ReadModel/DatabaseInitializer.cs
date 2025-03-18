using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;

namespace AspireEventSample.ApiService.Aggregates.ReadModel;

public class DatabaseInitializer
{
    private readonly BranchDbContext _dbContext;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(BranchDbContext dbContext, ILogger<DatabaseInitializer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing database...");
            
            // Check database connection
            if (await _dbContext.Database.CanConnectAsync())
            {
                _logger.LogInformation("Database connection successful.");
                
                // Perform any additional initialization if needed
                // For example, seeding initial data
                
                _logger.LogInformation("Database initialization completed successfully.");
            }
            else
            {
                _logger.LogWarning("Could not connect to the database.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }
}
