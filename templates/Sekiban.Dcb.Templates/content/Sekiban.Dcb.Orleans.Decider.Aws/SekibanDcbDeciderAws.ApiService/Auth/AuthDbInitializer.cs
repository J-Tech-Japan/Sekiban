using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.Tags;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Orleans.Runtime;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;

namespace SekibanDcbDeciderAws.ApiService.Auth;

/// <summary>
///     Initializes the authentication database with schema and seed data.
/// </summary>
public class AuthDbInitializer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuthDbInitializer> _logger;
    private readonly IClusterClient _clusterClient;
    private const int MaxRetries = 10;
    private const int RetryDelayMs = 2000;
    private const int OrleansWaitDelayMs = 1000;
    private const int MaxOrleansWaitRetries = 30;

    public AuthDbInitializer(
        IServiceProvider serviceProvider,
        ILogger<AuthDbInitializer> logger,
        IClusterClient clusterClient)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _clusterClient = clusterClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Orleans Silo to be fully ready before accessing grains
        await WaitForOrleansAsync(stoppingToken);

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var executor = scope.ServiceProvider.GetRequiredService<ISekibanExecutor>();

        // Wait for database to be ready with retries
        for (var i = 0; i < MaxRetries; i++)
        {
            try
            {
                _logger.LogInformation("Attempting to connect to database (attempt {Attempt}/{MaxRetries})...", i + 1, MaxRetries);

                // First, ensure we can connect
                if (!await context.Database.CanConnectAsync(stoppingToken))
                {
                    _logger.LogWarning("Cannot connect to database, waiting...");
                    await Task.Delay(RetryDelayMs, stoppingToken);
                    continue;
                }

                // Create Identity tables using raw SQL
                await CreateIdentityTablesAsync(context, stoppingToken);

                // Seed roles and users
                await SeedRolesAsync(roleManager);
                await SeedUsersAsync(userManager, executor);

                _logger.LogInformation("Authentication database initialization completed successfully");
                return;
            }
            catch (Exception ex) when (i < MaxRetries - 1)
            {
                _logger.LogWarning(ex, "Database initialization attempt {Attempt} failed, retrying...", i + 1);
                await Task.Delay(RetryDelayMs, stoppingToken);
            }
        }

        _logger.LogError("Failed to initialize authentication database after {MaxRetries} attempts", MaxRetries);
    }

    private async Task WaitForOrleansAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Waiting for Orleans Silo to be ready...");

        for (var i = 0; i < MaxOrleansWaitRetries; i++)
        {
            try
            {
                // Check if the silo membership is available
                var managementGrain = _clusterClient.GetGrain<IManagementGrain>(0);
                var hosts = await managementGrain.GetHosts(onlyActive: true);

                if (hosts != null && hosts.Count > 0)
                {
                    _logger.LogInformation("Orleans Silo is ready with {HostCount} active host(s)", hosts.Count);
                    // Add a longer delay to ensure grain storage providers are fully initialized
                    // The silo being active doesn't guarantee all storage providers are ready
                    _logger.LogInformation("Waiting additional time for grain storage providers to initialize...");
                    await Task.Delay(3000, cancellationToken);
                    _logger.LogInformation("Proceeding with user initialization");
                    return;
                }

                _logger.LogDebug("Orleans Silo not ready yet, waiting... (attempt {Attempt}/{MaxRetries})", i + 1, MaxOrleansWaitRetries);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Orleans not ready yet (attempt {Attempt}/{MaxRetries})", i + 1, MaxOrleansWaitRetries);
            }

            await Task.Delay(OrleansWaitDelayMs, cancellationToken);
        }

        _logger.LogWarning("Orleans Silo may not be fully ready after {MaxRetries} attempts, proceeding anyway", MaxOrleansWaitRetries);
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

    private async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, ISekibanExecutor executor)
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

                // Generate a valid GUID for the user ID to ensure it can be used with Sekiban
                var userId = Guid.CreateVersion7();
                var user = new ApplicationUser
                {
                    Id = userId.ToString(),
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
                    _logger.LogInformation("Sample user created: {Email} with ID: {UserId}", userData.Email, userId);
                    existingUser = user;
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
                _logger.LogInformation("Sample user already exists: {Email} with ID: {UserId}", userData.Email, existingUser.Id);
            }

            if (existingUser != null)
            {
                await EnsureUserDirectoryAsync(executor, existingUser);
                var roles = await userManager.GetRolesAsync(existingUser);
                await EnsureUserAccessAsync(executor, existingUser, roles);
            }
        }
    }

    private async Task EnsureUserDirectoryAsync(ISekibanExecutor executor, ApplicationUser user)
    {
        if (!Guid.TryParse(user.Id, out var userId))
        {
            _logger.LogWarning("Skipping user directory registration for {Email}: invalid user ID '{UserId}'.", user.Email, user.Id);
            return;
        }

        // Retry logic for grain storage initialization
        const int maxRetries = 5;
        const int retryDelayMs = 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var state = await executor.GetTagStateAsync<UserDirectoryProjector>(new UserTag(userId));
                _logger.LogInformation("User directory state for {Email}: {StateType}, Version: {Version}",
                    user.Email, state.Payload?.GetType().FullName ?? "null", state.Version);

                // Check if state is empty - either EmptyTagStatePayload (no events) or UserDirectoryEmpty
                var isEmpty = state.Payload is EmptyTagStatePayload || state.Payload is UserDirectoryState.UserDirectoryEmpty;
                if (isEmpty)
                {
                    _logger.LogInformation("Registering user in User Directory: {Email} ({UserId})", user.Email, userId);
                    await executor.ExecuteAsync(new RegisterUser
                    {
                        UserId = userId,
                        DisplayName = user.DisplayName ?? user.Email?.Split('@')[0] ?? "User",
                        Email = user.Email ?? string.Empty,
                        Department = null
                    });
                    _logger.LogInformation("Successfully registered user in User Directory: {Email}", user.Email);
                }
                else
                {
                    _logger.LogInformation("User already exists in User Directory: {Email}", user.Email);
                }
                return; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries && ex.Message.Contains("storage provider"))
            {
                _logger.LogWarning("Grain storage not ready for {Email}, retrying ({Attempt}/{MaxRetries})...", user.Email, attempt, maxRetries);
                await Task.Delay(retryDelayMs * attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register user directory entry for {Email}", user.Email);
                return;
            }
        }

        _logger.LogError("Failed to register user directory entry for {Email} after {MaxRetries} attempts", user.Email, maxRetries);
    }

    private async Task EnsureUserAccessAsync(
        ISekibanExecutor executor,
        ApplicationUser user,
        IList<string> roles)
    {
        if (!Guid.TryParse(user.Id, out var userId))
        {
            _logger.LogWarning("Skipping user access registration for {Email}: invalid user ID '{UserId}'.", user.Email, user.Id);
            return;
        }

        if (roles.Count == 0)
        {
            _logger.LogWarning("Skipping user access registration for {Email}: no roles found.", user.Email);
            return;
        }

        // Retry logic for grain storage initialization
        const int maxRetries = 5;
        const int retryDelayMs = 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var state = await executor.GetTagStateAsync<UserAccessProjector>(new UserAccessTag(userId));
                _logger.LogInformation("User access state for {Email}: {StateType}, Version: {Version}",
                    user.Email, state.Payload?.GetType().FullName ?? "null", state.Version);

                // Check if state is empty - either EmptyTagStatePayload (no events) or UserAccessEmpty
                var isEmpty = state.Payload is EmptyTagStatePayload || state.Payload is UserAccessState.UserAccessEmpty;
                if (isEmpty)
                {
                    _logger.LogInformation("Granting user access for {Email} with roles: {Roles}", user.Email, string.Join(", ", roles));
                    await executor.ExecuteAsync(new GrantUserAccess
                    {
                        UserId = userId,
                        InitialRole = roles[0]
                    });

                    foreach (var role in roles.Skip(1))
                    {
                        await executor.ExecuteAsync(new GrantUserRole
                        {
                            UserId = userId,
                            Role = role
                        });
                    }
                    _logger.LogInformation("Successfully granted user access for {Email}", user.Email);
                    return;
                }

                if (state.Payload is UserAccessState.UserAccessActive active)
                {
                    var missingRoles = roles.Where(role => !active.HasRole(role)).ToList();
                    if (missingRoles.Count > 0)
                    {
                        _logger.LogInformation("Adding missing roles for {Email}: {Roles}", user.Email, string.Join(", ", missingRoles));
                        foreach (var role in missingRoles)
                        {
                            await executor.ExecuteAsync(new GrantUserRole
                            {
                                UserId = userId,
                                Role = role
                            });
                        }
                    }
                    else
                    {
                        _logger.LogInformation("User access already complete for {Email}", user.Email);
                    }
                }
                return; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries && ex.Message.Contains("storage provider"))
            {
                _logger.LogWarning("Grain storage not ready for {Email} access, retrying ({Attempt}/{MaxRetries})...", user.Email, attempt, maxRetries);
                await Task.Delay(retryDelayMs * attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register user access entry for {Email}", user.Email);
                return;
            }
        }

        _logger.LogError("Failed to register user access entry for {Email} after {MaxRetries} attempts", user.Email, maxRetries);
    }
}
