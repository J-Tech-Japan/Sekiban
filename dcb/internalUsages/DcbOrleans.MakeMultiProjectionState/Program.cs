using System.CommandLine;
using System.Reflection;
using Dcb.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;

// Build configuration from environment variables and user secrets
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

// Create the root command
var rootCommand = new RootCommand("Multi Projection State Builder CLI - Pre-build projection states for faster startup");

// Database type option (shared across commands)
// Default from: --database option > Sekiban:Database config > DATABASE_TYPE env > "postgres"
var databaseOption = new Option<string?>(
    name: "--database",
    description: "Database type: postgres or cosmos (defaults to Sekiban:Database config or DATABASE_TYPE env var)");
databaseOption.AddAlias("-d");

// Connection string option for Postgres
var connectionStringOption = new Option<string?>(
    name: "--connection-string",
    description: "PostgreSQL connection string (defaults to ConnectionStrings:DcbPostgres config)");
connectionStringOption.AddAlias("-c");

// Connection string option for Cosmos
var cosmosConnectionStringOption = new Option<string?>(
    name: "--cosmos-connection-string",
    description: "Cosmos DB connection string (defaults to ConnectionStrings:SekibanDcbCosmos config)");

// Cosmos database name option
var cosmosDatabaseNameOption = new Option<string?>(
    name: "--cosmos-database",
    description: "Cosmos DB database name (defaults to CosmosDb:DatabaseName config or 'SekibanDcb')");

// Min events option
var minEventsOption = new Option<int>(
    name: "--min-events",
    getDefaultValue: () => int.TryParse(configuration["MIN_EVENTS"], out var val) ? val : 3000,
    description: "Minimum events required before building state");
minEventsOption.AddAlias("-m");

// Projector name option (optional - if not specified, build all)
var projectorNameOption = new Option<string?>(
    name: "--projector",
    getDefaultValue: () => configuration["PROJECTOR_NAME"],
    description: "Specific projector name to build (if not specified, builds all)");
projectorNameOption.AddAlias("-p");

// Force rebuild option
var forceRebuildOption = new Option<bool>(
    name: "--force",
    getDefaultValue: () => configuration["FORCE_REBUILD"]?.ToLowerInvariant() == "true",
    description: "Force rebuild even if state exists");
forceRebuildOption.AddAlias("-f");

// Verbose option
var verboseOption = new Option<bool>(
    name: "--verbose",
    getDefaultValue: () => configuration["VERBOSE"]?.ToLowerInvariant() == "true",
    description: "Show verbose output");
verboseOption.AddAlias("-v");

// Build command
var buildCommand = new Command("build", "Build multi projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    minEventsOption,
    projectorNameOption,
    forceRebuildOption,
    verboseOption
};

buildCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var minEvents = context.ParseResult.GetValueForOption(minEventsOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);
    var forceRebuild = context.ParseResult.GetValueForOption(forceRebuildOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await BuildProjectionStatesAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, minEvents, projectorName, forceRebuild, verbose);
});

// List command
var listCommand = new Command("list", "List all registered projectors");

listCommand.SetHandler(async () =>
{
    await ListProjectorsAsync();
});

// Status command
var statusCommand = new Command("status", "Show status of all projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption
};

statusCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowStatusAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName);
});

rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(statusCommand);

return await rootCommand.InvokeAsync(args);

// Helper to resolve database type
static string ResolveDatabaseType(IConfiguration config, string? cliOption)
{
    // Priority: CLI option > Sekiban:Database > DATABASE_TYPE env > "postgres"
    if (!string.IsNullOrEmpty(cliOption))
        return cliOption;

    var sekibanDatabase = config["Sekiban:Database"];
    if (!string.IsNullOrEmpty(sekibanDatabase))
        return sekibanDatabase;

    var envDatabaseType = config["DATABASE_TYPE"];
    if (!string.IsNullOrEmpty(envDatabaseType))
        return envDatabaseType;

    return "postgres";
}

// Helper to resolve Cosmos database name
static string ResolveCosmosDatabaseName(IConfiguration config, string? cliOption)
{
    // Priority: CLI option > CosmosDb:DatabaseName > COSMOS_DATABASE_NAME env > "SekibanDcb"
    if (!string.IsNullOrEmpty(cliOption))
        return cliOption;

    var cosmosDbName = config["CosmosDb:DatabaseName"];
    if (!string.IsNullOrEmpty(cosmosDbName))
        return cosmosDbName;

    var envDbName = config["COSMOS_DATABASE_NAME"];
    if (!string.IsNullOrEmpty(envDbName))
        return envDbName;

    return "SekibanDcb";
}

// Helper to resolve connection string from argument, config, or environment
static string ResolveConnectionString(IConfiguration config, string databaseType, string? connectionString, string? cosmosConnectionString)
{
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        // Priority: CLI option > ConnectionStrings:SekibanDcbCosmos > COSMOS_CONNECTION_STRING env
        if (!string.IsNullOrEmpty(cosmosConnectionString))
            return cosmosConnectionString;

        var configConnStr = config["ConnectionStrings:SekibanDcbCosmos"];
        if (!string.IsNullOrEmpty(configConnStr))
            return configConnStr;

        var envConnStr = config["COSMOS_CONNECTION_STRING"];
        if (!string.IsNullOrEmpty(envConnStr))
            return envConnStr;

        throw new InvalidOperationException(
            "Cosmos DB connection string not provided.\n" +
            "Options:\n" +
            "  1. Use --cosmos-connection-string option\n" +
            "  2. Set user secret: dotnet user-secrets set \"ConnectionStrings:SekibanDcbCosmos\" \"your-connection-string\"\n" +
            "  3. Set environment variable: COSMOS_CONNECTION_STRING");
    }
    else
    {
        // Priority: CLI option > ConnectionStrings:DcbPostgres > CONNECTION_STRING env
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        var configConnStr = config["ConnectionStrings:DcbPostgres"];
        if (!string.IsNullOrEmpty(configConnStr))
            return configConnStr;

        var envConnStr = config["CONNECTION_STRING"];
        if (!string.IsNullOrEmpty(envConnStr))
            return envConnStr;

        throw new InvalidOperationException(
            "PostgreSQL connection string not provided.\n" +
            "Options:\n" +
            "  1. Use --connection-string option\n" +
            "  2. Set user secret: dotnet user-secrets set \"ConnectionStrings:DcbPostgres\" \"your-connection-string\"\n" +
            "  3. Set environment variable: CONNECTION_STRING");
    }
}

// Implementation methods
static async Task BuildProjectionStatesAsync(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    int minEvents,
    string? projectorName,
    bool forceRebuild,
    bool verbose)
{
    Console.WriteLine("=== Multi Projection State Builder ===");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Connection: {connectionString[..Math.Min(50, connectionString.Length)]}...");
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        Console.WriteLine($"Cosmos Database: {cosmosDatabaseName}");
    }
    Console.WriteLine($"Min Events: {minEvents}");
    Console.WriteLine($"Force Rebuild: {forceRebuild}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var builder = services.GetRequiredService<MultiProjectionStateBuilder>();

    var options = new MultiProjectionBuildOptions
    {
        MinEventThreshold = minEvents,
        Force = forceRebuild,
        SpecificProjector = projectorName
    };

    if (!string.IsNullOrEmpty(projectorName))
    {
        Console.WriteLine($"Building projector: {projectorName}");
        var result = await builder.BuildProjectorAsync(projectorName, options);
        PrintBuildResult(result, verbose);
    }
    else
    {
        Console.WriteLine("Building all projectors...");
        var buildResult = await builder.BuildAllAsync(options);
        var results = buildResult.Results;
        Console.WriteLine($"\nResults: {results.Count} projector(s) processed");
        Console.WriteLine(new string('-', 80));

        foreach (var result in results)
        {
            PrintBuildResult(result, verbose);
        }

        var succeeded = results.Count(r => r.Status == BuildStatus.Success);
        var skipped = results.Count(r => r.Status == BuildStatus.Skipped);
        var failed = results.Count(r => r.Status == BuildStatus.Failed);

        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"Summary: {succeeded} succeeded, {skipped} skipped, {failed} failed");
    }
}

static async Task ListProjectorsAsync()
{
    Console.WriteLine("=== Registered Projectors ===\n");

    var domainTypes = DomainType.GetDomainTypes();
    var projectorNames = domainTypes.MultiProjectorTypes.GetAllProjectorNames();

    Console.WriteLine($"Total: {projectorNames.Count} projector(s)\n");

    foreach (var name in projectorNames)
    {
        var versionResult = domainTypes.MultiProjectorTypes.GetProjectorVersion(name);
        var version = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
        Console.WriteLine($"  - {name} (version: {version})");
    }

    await Task.CompletedTask;
}

static async Task ShowStatusAsync(string connectionString, string databaseType, string cosmosDatabaseName)
{
    Console.WriteLine("=== Projection State Status ===\n");
    Console.WriteLine($"Database: {databaseType}");
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        Console.WriteLine($"Cosmos Database: {cosmosDatabaseName}");
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();
    var eventStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Get total event count
    var eventCountResult = await eventStore.GetEventCountAsync();
    var totalEvents = eventCountResult.IsSuccess ? eventCountResult.GetValue() : 0;
    Console.WriteLine($"Total Events in Store: {totalEvents:N0}\n");

    // List all states
    var listResult = await stateStore.ListAllAsync();
    if (!listResult.IsSuccess)
    {
        Console.WriteLine($"Error listing states: {listResult.GetException().Message}");
        return;
    }

    var states = listResult.GetValue();
    if (states.Count == 0)
    {
        Console.WriteLine("No projection states found.\n");
    }
    else
    {
        Console.WriteLine($"{"Projector",-50} {"Version",-15} {"Events",-12} {"Size",-15} {"Updated"}");
        Console.WriteLine(new string('-', 120));

        foreach (var state in states)
        {
            var sizeStr = FormatBytes(state.CompressedSizeBytes);
            Console.WriteLine($"{state.ProjectorName,-50} {state.ProjectorVersion,-15} {state.EventsProcessed,-12:N0} {sizeStr,-15} {state.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        }
    }

    // Show registered projectors without state
    var projectorNames = domainTypes.MultiProjectorTypes.GetAllProjectorNames();
    var projectorsWithState = states.Select(s => s.ProjectorName).ToHashSet();
    var projectorsWithoutState = projectorNames.Where(n => !projectorsWithState.Contains(n)).ToList();

    if (projectorsWithoutState.Count > 0)
    {
        Console.WriteLine($"\nProjectors without stored state:");
        foreach (var name in projectorsWithoutState)
        {
            var versionResult = domainTypes.MultiProjectorTypes.GetProjectorVersion(name);
            var version = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
            Console.WriteLine($"  - {name} (version: {version})");
        }
    }
}

static void PrintBuildResult(ProjectorBuildResult result, bool verbose)
{
    var statusIcon = result.Status switch
    {
        BuildStatus.Success => "✓",
        BuildStatus.Skipped => "○",
        BuildStatus.Failed => "✗",
        _ => "?"
    };

    var statusColor = result.Status switch
    {
        BuildStatus.Success => ConsoleColor.Green,
        BuildStatus.Skipped => ConsoleColor.Yellow,
        BuildStatus.Failed => ConsoleColor.Red,
        _ => ConsoleColor.White
    };

    Console.ForegroundColor = statusColor;
    Console.Write($"[{statusIcon}] ");
    Console.ResetColor();

    Console.WriteLine($"{result.ProjectorName} (v{result.ProjectorVersion})");

    if (verbose || result.Status == BuildStatus.Failed)
    {
        Console.WriteLine($"    Status: {result.Status}");
        Console.WriteLine($"    Reason: {result.Reason}");
        Console.WriteLine($"    Events Processed: {result.EventsProcessed:N0}");
    }
}

static string FormatBytes(long bytes)
{
    string[] sizes = ["B", "KB", "MB", "GB"];
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < sizes.Length - 1)
    {
        order++;
        len /= 1024;
    }
    return $"{len:0.##} {sizes[order]}";
}

static ServiceProvider BuildServices(string connectionString, string databaseType, string cosmosDatabaseName)
{
    var services = new ServiceCollection();

    // Register domain types
    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        // Register Cosmos DB services
        var cosmosClient = new CosmosClient(connectionString);
        var cosmosContext = new CosmosDbContext(cosmosClient, cosmosDatabaseName);
        services.AddSingleton(cosmosContext);
        services.AddSingleton<IEventStore, CosmosDbEventStore>();
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();
    }
    else
    {
        // Register Postgres DbContext factory
        services.AddPooledDbContextFactory<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Register event store
        services.AddSingleton<IEventStore, PostgresEventStore>();

        // Register multi projection state store
        services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();
    }

    // Register the builder
    services.AddSingleton<MultiProjectionStateBuilder>();

    return services.BuildServiceProvider();
}
