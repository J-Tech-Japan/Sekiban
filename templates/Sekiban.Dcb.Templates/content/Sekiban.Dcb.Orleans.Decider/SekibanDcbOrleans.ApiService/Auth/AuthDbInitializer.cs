using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace SekibanDcbOrleans.ApiService.Auth;

/// <summary>
///     Initializes the authentication database with schema and seed data.
/// </summary>
public class AuthDbInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthDbInitializer> _logger;
    private const int MaxRetries = 10;
    private const int RetryDelayMs = 2000;

    public AuthDbInitializer(IServiceProvider serviceProvider, ILogger<AuthDbInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // Wait for database to be ready with retries
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                _logger.LogInformation("Attempting to connect to database (attempt {Attempt}/{MaxRetries})...", i + 1, MaxRetries);

                // First, ensure we can connect
                if (!await context.Database.CanConnectAsync(cancellationToken))
                {
                    _logger.LogWarning("Cannot connect to database, waiting...");
                    await Task.Delay(RetryDelayMs, cancellationToken);
                    continue;
                }

                // Create Identity tables using raw SQL
                await CreateIdentityTablesAsync(context, cancellationToken);

                // Seed roles and users
                await SeedRolesAsync(roleManager);
                await SeedUsersAsync(userManager);

                _logger.LogInformation("Authentication database initialization completed successfully");
                return;
            }
            catch (Exception ex) when (i < MaxRetries - 1)
            {
                _logger.LogWarning(ex, "Database initialization attempt {Attempt} failed, retrying...", i + 1);
                await Task.Delay(RetryDelayMs, cancellationToken);
            }
        }

        _logger.LogError("Failed to initialize authentication database after {MaxRetries} attempts", MaxRetries);
        throw new InvalidOperationException($"Failed to initialize authentication database after {MaxRetries} attempts");
    }

    private async Task CreateIdentityTablesAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        // Create the identity schema if it doesn't exist
        await context.Database.ExecuteSqlRawAsync(
            "CREATE SCHEMA IF NOT EXISTS identity",
            cancellationToken);
        _logger.LogInformation("Identity schema ensured");

        // Check if AspNetRoles table exists
        var tableExists = await CheckTableExistsAsync(context, "identity", "AspNetRoles", cancellationToken);
        if (tableExists)
        {
            _logger.LogInformation("Identity tables already exist");
            return;
        }

        _logger.LogInformation("Creating Identity tables...");

        // Generate and execute the SQL script from the model
        var databaseCreator = context.Database.GetService<IRelationalDatabaseCreator>();
        var createScript = databaseCreator.GenerateCreateScript();

        // Execute the create script
        await context.Database.ExecuteSqlRawAsync(createScript, cancellationToken);

        _logger.LogInformation("Identity tables created successfully");
    }

    private static async Task<bool> CheckTableExistsAsync(
        ApplicationDbContext context,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = $@"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = '{schema}'
                AND table_name = '{tableName}'
            )";

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool b && b;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = ["Admin", "User"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                _logger.LogInformation("Creating role: {Role}", role);
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private async Task SeedUsersAsync(UserManager<ApplicationUser> userManager)
    {
        // Sample users for demonstration
        var sampleUsers = new[]
        {
            new { Email = "user1@example.com", DisplayName = "User One", Role = "User" },
            new { Email = "user2@example.com", DisplayName = "User Two", Role = "User" },
            new { Email = "user3@example.com", DisplayName = "User Three", Role = "User" },
            new { Email = "admin@example.com", DisplayName = "Administrator", Role = "Admin" }
        };

        const string defaultPassword = "Sekiban1234%";

        foreach (var userData in sampleUsers)
        {
            var existingUser = await userManager.FindByEmailAsync(userData.Email);
            if (existingUser == null)
            {
                _logger.LogInformation("Creating sample user: {Email}", userData.Email);

                var user = new ApplicationUser
                {
                    UserName = userData.Email,
                    Email = userData.Email,
                    DisplayName = userData.DisplayName,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, defaultPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, userData.Role);
                    // Also add User role to admin
                    if (userData.Role == "Admin")
                    {
                        await userManager.AddToRoleAsync(user, "User");
                    }
                    _logger.LogInformation("Sample user created: {Email}", userData.Email);
                }
                else
                {
                    _logger.LogError("Failed to create user {Email}: {Errors}",
                        userData.Email,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                _logger.LogInformation("Sample user already exists: {Email}", userData.Email);
            }
        }
    }
}
