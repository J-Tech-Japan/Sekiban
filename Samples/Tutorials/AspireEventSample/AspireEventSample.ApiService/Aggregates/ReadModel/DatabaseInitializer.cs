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
            _logger.LogInformation("Ensuring database is created...");
            
            // Create the database if it doesn't exist
            await _dbContext.Database.EnsureCreatedAsync();
            
            _logger.LogInformation("Database initialization completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }
}
