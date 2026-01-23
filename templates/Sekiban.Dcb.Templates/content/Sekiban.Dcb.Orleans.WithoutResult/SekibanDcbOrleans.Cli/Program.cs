using System.CommandLine;
using System.Reflection;
using Dcb.Domain.WithoutResult;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Services;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Sqlite.Services;
using Sekiban.Dcb.Storage;

// Build configuration from environment variables and user secrets
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

// Create the root command
var rootCommand = new RootCommand(
@"Sekiban DCB CLI - Manage projections, tags, and events

Examples:
  # List all registered projectors and tag groups
  dotnet run -- list

  # Show status of all projection states
  dotnet run -- status -c ""Host=localhost;Database=mydb;Username=user;Password=pass""

  # Build all projection states
  dotnet run -- build -c ""Host=localhost;Database=mydb;Username=user;Password=pass""

  # Fetch events for a specific tag
  dotnet run -- tag-events -t ""WeatherForecast:00000000-0000-0000-0000-000000000001"" -c ""...""

  # Project tag state using a specific projector
  dotnet run -- tag-state -t ""WeatherForecast:guid-here"" -P ""WeatherForecastProjector"" -c ""...""

  # Save projection state to JSON files
  dotnet run -- save -o ./output -c ""...""

  # List all tags in the event store
  dotnet run -- tag-list -c ""..."" -o ./output

  # List tags filtered by group
  dotnet run -- tag-list -g ""WeatherForecast"" -c ""...""

  # Use Cosmos DB instead of PostgreSQL
  dotnet run -- status -d cosmos --cosmos-connection-string ""AccountEndpoint=...""");

// Database type option (shared across commands)
var databaseOption = new Option<string?>("--database", "-d")
{
    Description = "Database type: postgres or cosmos (defaults to Sekiban:Database config or DATABASE_TYPE env var)"
};

// Connection string option for Postgres
var connectionStringOption = new Option<string?>("--connection-string", "-c")
{
    Description = "PostgreSQL connection string (defaults to ConnectionStrings:DcbPostgres config)"
};

// Connection string option for Cosmos
var cosmosConnectionStringOption = new Option<string?>("--cosmos-connection-string")
{
    Description = "Cosmos DB connection string (defaults to ConnectionStrings:SekibanDcbCosmos config)"
};

// Cosmos database name option
var cosmosDatabaseNameOption = new Option<string?>("--cosmos-database")
{
    Description = "Cosmos DB database name (defaults to CosmosDb:DatabaseName config or 'SekibanDcb')"
};

// Min events option
var minEventsOption = new Option<int>("--min-events", "-m")
{
    Description = "Minimum events required before building state",
    DefaultValueFactory = _ => int.TryParse(configuration["MIN_EVENTS"], out var val) ? val : 3000
};

// Projector name option
var projectorNameOption = new Option<string?>("--projector", "-p")
{
    Description = "Specific projector name to build (if not specified, builds all)",
    DefaultValueFactory = _ => configuration["PROJECTOR_NAME"]
};

// Force rebuild option
var forceRebuildOption = new Option<bool>("--force", "-f")
{
    Description = "Force rebuild even if state exists",
    DefaultValueFactory = _ => configuration["FORCE_REBUILD"]?.ToLowerInvariant() == "true"
};

// Verbose option
var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Show verbose output",
    DefaultValueFactory = _ => configuration["VERBOSE"]?.ToLowerInvariant() == "true"
};

// Cache mode option
var cacheModeOption = new Option<string?>("--cache-mode")
{
    Description = "Cache mode: auto | off | clear | cache-only (default: auto)",
    DefaultValueFactory = _ => configuration["CACHE_MODE"] ?? "auto"
};

// Cache directory option
var cacheDirOption = new Option<string>("--cache-dir", "-C")
{
    Description = "Directory for local SQLite cache",
    DefaultValueFactory = _ => configuration["CACHE_DIR"] ?? "./cache"
};

// Safe window option (minutes)
var safeWindowOption = new Option<int>("--safe-window")
{
    Description = "Safe window in minutes (events within this window are not cached)",
    DefaultValueFactory = _ => int.TryParse(configuration["SAFE_WINDOW_MINUTES"], out var val) ? val : 10
};

// Output directory option
var outputDirOption = new Option<string?>("--output-dir", "-o")
{
    Description = "Output directory for saved files",
    DefaultValueFactory = _ => configuration["OUTPUT_DIR"] ?? "./output"
};

// Tag option
var tagOption = new Option<string>("--tag", "-t")
{
    Description = "Tag string in format 'group:content' (e.g., 'WeatherForecast:00000000-0000-0000-0000-000000000001')",
    Required = true
};

// Tag projector option (optional - auto-detects from tag group if not specified)
var tagProjectorOption = new Option<string?>("--tag-projector", "-P")
{
    Description = "Tag projector name to use for projection. Auto-detects if not specified (e.g., 'WeatherForecast' tag uses 'WeatherForecastProjector')"
};

// Build command
var buildCommand = new Command("build", "Build multi projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheModeOption,
    cacheDirOption,
    minEventsOption,
    projectorNameOption,
    forceRebuildOption,
    verboseOption
};

buildCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheMode = parseResult.GetValue(cacheModeOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);
    var minEvents = parseResult.GetValue(minEventsOption);
    var projectorName = parseResult.GetValue(projectorNameOption);
    var forceRebuild = parseResult.GetValue(forceRebuildOption);
    var verbose = parseResult.GetValue(verboseOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);
    var cacheOptions = BuildCacheOptions(cacheMode, cacheDir);

    await BuildProjectionStatesAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, minEvents, projectorName, forceRebuild, verbose, cacheOptions);
});

// List command
var listCommand = new Command("list", "List all registered projectors");
listCommand.SetAction(async (parseResult, cancellationToken) => await ListProjectorsAsync());

// Status command
var statusCommand = new Command("status", "Show status of all projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheModeOption,
    cacheDirOption
};

statusCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheMode = parseResult.GetValue(cacheModeOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);
    var cacheOptions = BuildCacheOptions(cacheMode, cacheDir);

    await ShowStatusAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, cacheOptions);
});

// Save command
var saveCommand = new Command("save", "Save projection state JSON to files for debugging")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption,
    outputDirOption
};

saveCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var projectorName = parseResult.GetValue(projectorNameOption);
    var outputDir = parseResult.GetValue(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await SaveStateJsonAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName, outputDir);
});

// Delete command
var deleteCommand = new Command("delete", "Delete projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

deleteCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var projectorName = parseResult.GetValue(projectorNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await DeleteStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName);
});

// Tag-events command
var tagEventsCommand = new Command("tag-events", "Fetch and save all events for a specific tag")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheModeOption,
    cacheDirOption,
    tagOption,
    outputDirOption
};

tagEventsCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheMode = parseResult.GetValue(cacheModeOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);
    var tag = parseResult.GetValue(tagOption) ?? "";
    var outputDir = parseResult.GetValue(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);
    var cacheOptions = BuildCacheOptions(cacheMode, cacheDir);

    await FetchTagEventsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, outputDir, cacheOptions);
});

// Projection command
var projectionCommand = new Command("projection", "Display the current state of a projection")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

projectionCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var projectorName = parseResult.GetValue(projectorNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowProjectionAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName);
});

// Tag-state command
var tagStateCommand = new Command("tag-state", "Project and display the current state for a specific tag")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheModeOption,
    cacheDirOption,
    tagOption,
    tagProjectorOption
};

tagStateCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheMode = parseResult.GetValue(cacheModeOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);
    var tag = parseResult.GetValue(tagOption) ?? "";
    var tagProjector = parseResult.GetValue(tagProjectorOption) ?? "";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);
    var cacheOptions = BuildCacheOptions(cacheMode, cacheDir);

    await ShowTagStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, tagProjector, cacheOptions);
});

// Tag-list command
var tagGroupOption = new Option<string?>("--tag-group", "-g")
{
    Description = "Filter by tag group name (e.g., 'WeatherForecast')"
};

var tagListCommand = new Command("tag-list", "List all tags in the event store and export to JSON")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheModeOption,
    cacheDirOption,
    tagGroupOption,
    outputDirOption
};

tagListCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheMode = parseResult.GetValue(cacheModeOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);
    var tagGroup = parseResult.GetValue(tagGroupOption);
    var outputDir = parseResult.GetValue(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);
    var cacheOptions = BuildCacheOptions(cacheMode, cacheDir);

    await ListTagsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tagGroup, outputDir, cacheOptions);
});

// Cache-sync command
var cacheSyncCommand = new Command("cache-sync", "Sync remote events to local SQLite cache")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheDirOption,
    safeWindowOption,
    verboseOption
};

cacheSyncCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var cacheDir = parseResult.GetValue(cacheDirOption);
    var safeWindowMinutes = parseResult.GetValue(safeWindowOption);
    var verbose = parseResult.GetValue(verboseOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await SyncCacheAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, cacheDir ?? "./cache", safeWindowMinutes, verbose);
});

// Cache-stats command
var cacheStatsCommand = new Command("cache-stats", "Show local cache statistics")
{
    cacheDirOption
};

cacheStatsCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var cacheDir = parseResult.GetValue(cacheDirOption) ?? "./cache";
    await ShowCacheStatsAsync(cacheDir);
});

// Cache-clear command
var cacheClearCommand = new Command("cache-clear", "Clear local SQLite cache")
{
    cacheDirOption
};

cacheClearCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var cacheDir = parseResult.GetValue(cacheDirOption) ?? "./cache";
    await ClearCacheAsync(cacheDir);
});

rootCommand.Subcommands.Add(buildCommand);
rootCommand.Subcommands.Add(listCommand);
rootCommand.Subcommands.Add(statusCommand);
rootCommand.Subcommands.Add(saveCommand);
rootCommand.Subcommands.Add(deleteCommand);
rootCommand.Subcommands.Add(tagEventsCommand);
rootCommand.Subcommands.Add(projectionCommand);
rootCommand.Subcommands.Add(tagStateCommand);
rootCommand.Subcommands.Add(tagListCommand);
rootCommand.Subcommands.Add(cacheSyncCommand);
rootCommand.Subcommands.Add(cacheStatsCommand);
rootCommand.Subcommands.Add(cacheClearCommand);

return await rootCommand.Parse(args).InvokeAsync();

// Helper to resolve database type
static string ResolveDatabaseType(IConfiguration config, string? cliOption)
{
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

// Helper to resolve connection string
static string ResolveConnectionString(IConfiguration config, string databaseType, string? connectionString, string? cosmosConnectionString)
{
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
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

static CacheOptions BuildCacheOptions(string? cacheMode, string? cacheDir)
{
    var resolvedMode = cacheMode?.ToLowerInvariant() switch
    {
        "off" => SimpleCacheMode.Off,
        "clear" => SimpleCacheMode.Clear,
        "cache-only" or "cacheonly" => SimpleCacheMode.CacheOnly,
        _ => SimpleCacheMode.Auto
    };

    var resolvedCacheDir = string.IsNullOrWhiteSpace(cacheDir) ? "./cache" : cacheDir;
    return new CacheOptions(resolvedMode, resolvedCacheDir);
}

static bool TryResolveCachePath(CacheOptions cacheOptions, out string? cachePath)
{
    var resolvedPath = Path.Combine(cacheOptions.CacheDir, "events.db");

    if (cacheOptions.Mode == SimpleCacheMode.Clear && File.Exists(resolvedPath))
    {
        Console.WriteLine("Clearing existing cache...");
        DeleteCacheFiles(resolvedPath);
    }

    var useCache = cacheOptions.Mode != SimpleCacheMode.Off &&
                   cacheOptions.Mode != SimpleCacheMode.Clear &&
                   File.Exists(resolvedPath);

    if (cacheOptions.Mode == SimpleCacheMode.CacheOnly && !useCache)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: SQLite cache not found: {resolvedPath}");
        Console.WriteLine("Run 'cache-sync' first to create the cache.");
        Console.ResetColor();
        cachePath = null;
        return false;
    }

    cachePath = useCache ? resolvedPath : null;
    return true;
}

static bool TryReportCacheUsage(CacheOptions cacheOptions, out string? cachePath)
{
    if (!TryResolveCachePath(cacheOptions, out cachePath))
    {
        return false;
    }

    if (cacheOptions.Mode == SimpleCacheMode.CacheOnly)
    {
        Console.WriteLine("Mode: cache-only (SQLite cache only)");
    }

    if (cachePath != null)
    {
        Console.WriteLine($"Using SQLite cache: {cachePath}");
    }
    else if (cacheOptions.Mode != SimpleCacheMode.Off)
    {
        Console.WriteLine("No cache found, using remote database...");
    }

    return true;
}

static void DeleteCacheFiles(string cachePath)
{
    if (File.Exists(cachePath))
    {
        File.Delete(cachePath);
    }

    var walPath = cachePath + "-wal";
    if (File.Exists(walPath))
    {
        File.Delete(walPath);
    }

    var shmPath = cachePath + "-shm";
    if (File.Exists(shmPath))
    {
        File.Delete(shmPath);
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
    bool verbose,
    CacheOptions cacheOptions)
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

    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return;
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName, cachePath: cachePath);
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

    Console.WriteLine("\n=== Registered Tag Projectors ===\n");
    var tagProjectorNames = domainTypes.TagProjectorTypes.GetAllProjectorNames();
    Console.WriteLine($"Total: {tagProjectorNames.Count} tag projector(s)\n");

    foreach (var name in tagProjectorNames)
    {
        var versionResult = domainTypes.TagProjectorTypes.GetProjectorVersion(name);
        var version = versionResult.IsSuccess ? versionResult.GetValue() : "unknown";
        Console.WriteLine($"  - {name} (version: {version})");
    }

    Console.WriteLine("\n=== Registered Tag Groups ===\n");
    var tagGroupNames = domainTypes.TagTypes.GetAllTagGroupNames();
    Console.WriteLine($"Total: {tagGroupNames.Count} tag group(s)\n");

    foreach (var name in tagGroupNames)
    {
        Console.WriteLine($"  - {name}");
    }

    await Task.CompletedTask;
}

static async Task ShowStatusAsync(string connectionString, string databaseType, string cosmosDatabaseName, CacheOptions cacheOptions)
{
    Console.WriteLine("=== Projection State Status ===\n");
    Console.WriteLine($"Database: {databaseType}");
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        Console.WriteLine($"Cosmos Database: {cosmosDatabaseName}");
    }
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return;
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName, cachePath: cachePath);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();
    var eventStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    var eventCountResult = await eventStore.GetEventCountAsync();
    var totalEvents = eventCountResult.IsSuccess ? eventCountResult.GetValue() : 0;
    Console.WriteLine($"Total Events in Store: {totalEvents:N0}\n");

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
        BuildStatus.Success => "[OK]",
        BuildStatus.Skipped => "[SKIP]",
        BuildStatus.Failed => "[FAIL]",
        _ => "[?]"
    };

    Console.WriteLine($"{statusIcon} {result.ProjectorName} (v{result.ProjectorVersion})");

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

static async Task SaveStateJsonAsync(string connectionString, string databaseType, string cosmosDatabaseName, string? projectorName, string outputDir)
{
    Console.WriteLine("=== Save Projection State JSON ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();

    Directory.CreateDirectory(outputDir);

    var listResult = await stateStore.ListAllAsync();
    if (!listResult.IsSuccess)
    {
        Console.WriteLine($"Error listing states: {listResult.GetException().Message}");
        return;
    }

    var stateInfos = listResult.GetValue();
    if (!string.IsNullOrEmpty(projectorName))
    {
        stateInfos = stateInfos.Where(s => s.ProjectorName == projectorName).ToList();
    }

    if (stateInfos.Count == 0)
    {
        Console.WriteLine("No projection states found.");
        return;
    }

    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var savedCount = 0;

    foreach (var info in stateInfos)
    {
        Console.WriteLine($"Saving: {info.ProjectorName} (v{info.ProjectorVersion})");

        var recordResult = await stateStore.GetLatestForVersionAsync(info.ProjectorName, info.ProjectorVersion);
        if (!recordResult.IsSuccess || !recordResult.GetValue().HasValue)
        {
            Console.WriteLine($"  Error: Could not fetch full record");
            continue;
        }

        var state = recordResult.GetValue().GetValue();

        var metadataFileName = $"{state.ProjectorName}_{timestamp}_metadata.json";
        var metadataPath = Path.Combine(outputDir, metadataFileName);
        var metadata = new
        {
            state.ProjectorName,
            state.ProjectorVersion,
            state.PayloadType,
            state.LastSortableUniqueId,
            state.EventsProcessed,
            state.IsOffloaded,
            state.OffloadKey,
            state.OriginalSizeBytes,
            state.CompressedSizeBytes,
            state.SafeWindowThreshold,
            state.CreatedAt,
            state.UpdatedAt,
            state.BuildSource,
            state.BuildHost
        };
        await File.WriteAllTextAsync(metadataPath, System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"  Metadata: {metadataFileName}");

        if (state.StateData != null)
        {
            var stateFileName = $"{state.ProjectorName}_{timestamp}_state.json";
            var statePath = Path.Combine(outputDir, stateFileName);

            try
            {
                string jsonContent;

                if (state.StateData.Length >= 2 && state.StateData[0] == 0x1f && state.StateData[1] == 0x8b)
                {
                    jsonContent = GzipCompression.DecompressToString(state.StateData);
                }
                else
                {
                    jsonContent = System.Text.Encoding.UTF8.GetString(state.StateData);
                }

                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var prettyJson = System.Text.Json.JsonSerializer.Serialize(jsonDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(statePath, prettyJson);
                Console.WriteLine($"  State: {stateFileName} ({FormatBytes(state.StateData.LongLength)})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error decompressing state: {ex.Message}");
                var rawFileName = $"{state.ProjectorName}_{timestamp}_state.bin";
                var rawPath = Path.Combine(outputDir, rawFileName);
                await File.WriteAllBytesAsync(rawPath, state.StateData);
                Console.WriteLine($"  Raw state saved: {rawFileName}");
            }
        }
        else if (state.IsOffloaded)
        {
            Console.WriteLine($"  State is offloaded to: {state.OffloadKey}");
        }

        Console.WriteLine();
        savedCount++;
    }

    Console.WriteLine($"Saved {savedCount} projection state(s) to {outputDir}");
}

static async Task DeleteStateAsync(string connectionString, string databaseType, string cosmosDatabaseName, string? projectorName)
{
    Console.WriteLine("=== Delete Projection States ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();

    var listResult = await stateStore.ListAllAsync();
    if (!listResult.IsSuccess)
    {
        Console.WriteLine($"Error listing states: {listResult.GetException().Message}");
        return;
    }

    var states = listResult.GetValue();
    if (!string.IsNullOrEmpty(projectorName))
    {
        states = states.Where(s => s.ProjectorName == projectorName).ToList();
    }

    if (states.Count == 0)
    {
        Console.WriteLine("No projection states found to delete.");
        return;
    }

    Console.WriteLine($"Found {states.Count} state(s) to delete:");
    foreach (var state in states)
    {
        Console.WriteLine($"  - {state.ProjectorName} (v{state.ProjectorVersion})");
    }
    Console.WriteLine();

    Console.Write("Are you sure you want to delete these states? (y/N): ");
    var response = Console.ReadLine();
    if (response?.ToLowerInvariant() != "y")
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    var deleted = 0;
    foreach (var state in states)
    {
        var deleteResult = await stateStore.DeleteAsync(state.ProjectorName, state.ProjectorVersion);
        if (deleteResult.IsSuccess)
        {
            Console.WriteLine($"  Deleted: {state.ProjectorName} (v{state.ProjectorVersion})");
            deleted++;
        }
        else
        {
            Console.WriteLine($"  Failed to delete {state.ProjectorName}: {deleteResult.GetException().Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Deleted {deleted} of {states.Count} state(s)");
}

static async Task FetchTagEventsAsync(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    string tagString,
    string outputDir,
    CacheOptions cacheOptions)
{
    Console.WriteLine("=== Fetch Tag Events ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine($"Output Directory: {outputDir}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return;
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName, cachePath: cachePath);
    var tagEventService = services.GetRequiredService<TagEventService>();

    var tag = tagEventService.ParseTag(tagString);
    Console.WriteLine($"Parsed Tag: {tag.GetType().Name} = {tag}");
    Console.WriteLine();

    Console.WriteLine("Fetching events...");
    var eventsResult = await tagEventService.GetEventsByTagAsync(tag);
    if (!eventsResult.IsSuccess)
    {
        Console.WriteLine($"Error fetching events: {eventsResult.GetException().Message}");
        return;
    }

    var events = eventsResult.GetValue().ToList();
    Console.WriteLine($"Found {events.Count} event(s)\n");

    if (events.Count == 0)
    {
        Console.WriteLine("No events to save.");
        return;
    }

    Directory.CreateDirectory(outputDir);

    var eventsForJson = events.Select(e =>
    {
        var payloadJsonString = System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType(), tagEventService.JsonSerializerOptions);
        // Parse the JSON string back to JsonElement for proper serialization
        var payloadElement = System.Text.Json.JsonDocument.Parse(payloadJsonString).RootElement;
        return new
        {
            e.Id,
            SortableUniqueId = e.SortableUniqueIdValue,
            e.EventType,
            e.Tags,
            Payload = payloadElement
        };
    }).ToList();

    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var safeTagName = tagString.Replace(":", "_").Replace("/", "_");
    var fileName = $"tag_events_{safeTagName}_{timestamp}.json";
    var filePath = Path.Combine(outputDir, fileName);

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    var output = new
    {
        Tag = tagString,
        EventCount = events.Count,
        FetchedAt = DateTime.UtcNow,
        Events = eventsForJson
    };

    await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(output, jsonOptions));

    Console.WriteLine($"Events saved to: {filePath}");

    Console.WriteLine("\nEvent Summary:");
    Console.WriteLine($"{"#",-4} {"SortableUniqueId",-40} {"EventType",-40}");
    Console.WriteLine(new string('-', 90));

    var index = 1;
    foreach (var evt in events.Take(20))
    {
        Console.WriteLine($"{index,-4} {evt.SortableUniqueIdValue,-40} {evt.EventType,-40}");
        index++;
    }

    if (events.Count > 20)
    {
        Console.WriteLine($"... and {events.Count - 20} more events");
    }
}

static async Task ShowProjectionAsync(string connectionString, string databaseType, string cosmosDatabaseName, string? projectorName)
{
    Console.WriteLine("=== Projection State ===\n");
    Console.WriteLine($"Database: {databaseType}");
    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        Console.WriteLine($"Cosmos Database: {cosmosDatabaseName}");
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    var listResult = await stateStore.ListAllAsync();
    if (!listResult.IsSuccess)
    {
        Console.WriteLine($"Error listing states: {listResult.GetException().Message}");
        return;
    }

    var stateInfos = listResult.GetValue();
    if (!string.IsNullOrEmpty(projectorName))
    {
        stateInfos = stateInfos.Where(s => s.ProjectorName == projectorName).ToList();
    }

    if (stateInfos.Count == 0)
    {
        Console.WriteLine("No projection states found.");

        var projectorNames = domainTypes.MultiProjectorTypes.GetAllProjectorNames();
        if (projectorNames.Count > 0)
        {
            Console.WriteLine("\nAvailable projectors:");
            foreach (var name in projectorNames)
            {
                Console.WriteLine($"  - {name}");
            }
        }
        return;
    }

    foreach (var info in stateInfos)
    {
        Console.WriteLine($"Projector: {info.ProjectorName}");
        Console.WriteLine($"  Version: {info.ProjectorVersion}");
        Console.WriteLine($"  Events Processed: {info.EventsProcessed:N0}");
        Console.WriteLine($"  Size: {FormatBytes(info.CompressedSizeBytes)}");
        Console.WriteLine($"  Updated: {info.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        var recordResult = await stateStore.GetLatestForVersionAsync(info.ProjectorName, info.ProjectorVersion);
        if (!recordResult.IsSuccess || !recordResult.GetValue().HasValue)
        {
            Console.WriteLine("  Could not fetch full state record.");
            continue;
        }

        var state = recordResult.GetValue().GetValue();

        Console.WriteLine($"  Last SortableUniqueId: {state.LastSortableUniqueId}");
        Console.WriteLine($"  Safe Window Threshold: {state.SafeWindowThreshold}");
        Console.WriteLine($"  Build Source: {state.BuildSource}");
        Console.WriteLine($"  Build Host: {state.BuildHost}");
        Console.WriteLine($"  Is Offloaded: {state.IsOffloaded}");

        if (state.StateData != null)
        {
            try
            {
                string jsonContent;

                if (state.StateData.Length >= 2 && state.StateData[0] == 0x1f && state.StateData[1] == 0x8b)
                {
                    jsonContent = GzipCompression.DecompressToString(state.StateData);
                }
                else
                {
                    jsonContent = System.Text.Encoding.UTF8.GetString(state.StateData);
                }

                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);

                Console.WriteLine("\n  State Preview:");

                if (jsonDoc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var itemCount = items.GetArrayLength();
                    Console.WriteLine($"    Items Count: {itemCount}");

                    var shown = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (shown >= 5) break;
                        if (item.TryGetProperty("id", out var id))
                        {
                            var idStr = id.GetGuid().ToString();
                            Console.WriteLine($"    - {idStr}");
                        }
                        shown++;
                    }
                    if (itemCount > 5)
                    {
                        Console.WriteLine($"    ... and {itemCount - 5} more items");
                    }
                }
                else if (jsonDoc.RootElement.TryGetProperty("State", out var stateElement))
                {
                    Console.WriteLine($"    State Type: {stateElement.ValueKind}");
                }
                else
                {
                    Console.WriteLine("    Root Properties:");
                    foreach (var prop in jsonDoc.RootElement.EnumerateObject().Take(5))
                    {
                        Console.WriteLine($"    - {prop.Name}: {prop.Value.ValueKind}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Could not parse state data: {ex.Message}");
            }
        }

        Console.WriteLine();
    }
}

static async Task ShowTagStateAsync(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    string tagString,
    string? projectorName,
    CacheOptions cacheOptions)
{
    Console.WriteLine("=== Tag State Projection ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return;
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName, cachePath: cachePath);
    var tagStateService = services.GetRequiredService<TagStateService>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Auto-detect projector name from tag group if not specified
    var resolvedProjectorName = projectorName;
    if (string.IsNullOrEmpty(resolvedProjectorName))
    {
        // Extract tag group from tag string (e.g., "WeatherForecast:xxx" -> "WeatherForecast")
        var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
        resolvedProjectorName = domainTypes.TagProjectorTypes.TryGetProjectorForTagGroup(tagGroup);

        if (resolvedProjectorName != null)
        {
            Console.WriteLine($"Projector: {resolvedProjectorName} (auto-detected from tag group '{tagGroup}')");
        }
        else
        {
            Console.WriteLine($"Projector: (not specified, could not auto-detect from '{tagGroup}')");
            Console.WriteLine("\nAvailable Tag Projectors:");
            foreach (var name in tagStateService.GetAllTagProjectorNames())
            {
                Console.WriteLine($"  - {name}");
            }
            Console.WriteLine("\nAvailable Tag Groups:");
            foreach (var name in tagStateService.GetAllTagGroupNames())
            {
                Console.WriteLine($"  - {name}");
            }
            return;
        }
    }
    else
    {
        Console.WriteLine($"Projector: {resolvedProjectorName}");
    }
    Console.WriteLine();

    Console.WriteLine("Projecting events...\n");
    var result = await tagStateService.ProjectTagStateAsync(tagString, resolvedProjectorName);

    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.GetException().Message}");

        Console.WriteLine("\nAvailable Tag Projectors:");
        foreach (var name in tagStateService.GetAllTagProjectorNames())
        {
            Console.WriteLine($"  - {name}");
        }
        return;
    }

    var projectionResult = result.GetValue();

    Console.WriteLine($"Tag: {projectionResult.Tag}");
    Console.WriteLine($"Projector: {projectionResult.ProjectorName} (v{projectionResult.ProjectorVersion})");
    Console.WriteLine($"Events Processed: {projectionResult.EventCount}");
    Console.WriteLine($"Last SortableUniqueId: {projectionResult.LastSortableUniqueId ?? "(none)"}");
    Console.WriteLine();

    Console.WriteLine("Projected State:");
    Console.WriteLine(new string('-', 60));

    var stateType = projectionResult.State.GetType();
    Console.WriteLine($"Type: {stateType.Name}");

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    var stateJson = System.Text.Json.JsonSerializer.Serialize(projectionResult.State, stateType, jsonOptions);
    Console.WriteLine(stateJson);
}

static ServiceProvider BuildServices(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    string? cachePath = null)
{
    var services = new ServiceCollection();

    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);

    var useCache = !string.IsNullOrWhiteSpace(cachePath);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        var cosmosClient = new CosmosClient(connectionString);
        var cosmosContext = new CosmosDbContext(cosmosClient, cosmosDatabaseName);
        services.AddSingleton(cosmosContext);
        if (useCache)
        {
            services.AddSingleton<IEventStore>(sp =>
                SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath!, sp.GetRequiredService<DcbDomainTypes>()));
        }
        else
        {
            services.AddSingleton<IEventStore, CosmosDbEventStore>();
        }
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();
    }
    else
    {
        services.AddPooledDbContextFactory<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        if (useCache)
        {
            services.AddSingleton<IEventStore>(sp =>
                SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath!, sp.GetRequiredService<DcbDomainTypes>()));
        }
        else
        {
            services.AddSingleton<IEventStore, PostgresEventStore>();
        }
        services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();
    }

    services.AddSingleton<MultiProjectionStateBuilder>();
    services.AddSingleton<TagEventService>();
    services.AddSingleton<TagStateService>();
    services.AddSingleton<TagListService>();

    return services.BuildServiceProvider();
}

static async Task ListTagsAsync(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    string? tagGroup,
    string outputDir,
    CacheOptions cacheOptions)
{
    Console.WriteLine("=== Tag List ===\n");
    Console.WriteLine($"Database: {databaseType}");
    if (!string.IsNullOrEmpty(tagGroup))
    {
        Console.WriteLine($"Filter: {tagGroup}");
    }
    Console.WriteLine($"Output Directory: {outputDir}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return;
    }
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName, cachePath: cachePath);
    var tagListService = services.GetRequiredService<TagListService>();

    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var fileName = string.IsNullOrEmpty(tagGroup)
        ? $"tag_list_{timestamp}.json"
        : $"tag_list_{tagGroup}_{timestamp}.json";
    var filePath = Path.Combine(outputDir, fileName);

    var result = await tagListService.ExportTagListAsync(filePath, tagGroup);
    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.GetException().Message}");
        return;
    }

    var exportResult = result.GetValue();

    Console.WriteLine($"Total Tag Groups: {exportResult.TotalTagGroups}");
    Console.WriteLine($"Total Tags: {exportResult.TotalTags}");
    Console.WriteLine($"Total Events: {exportResult.TotalEvents:N0}");
    Console.WriteLine();

    foreach (var group in exportResult.TagGroups)
    {
        Console.WriteLine($"[{group.TagGroup}] ({group.TagCount} tags, {group.TotalEvents:N0} events)");

        var tagsToShow = group.Tags.Take(10).ToList();
        foreach (var tag in tagsToShow)
        {
            Console.WriteLine($"  - {tag.Tag}");
            Console.WriteLine($"      Events: {tag.EventCount}, First: {tag.FirstEventAt:yyyy-MM-dd HH:mm:ss}, Last: {tag.LastEventAt:yyyy-MM-dd HH:mm:ss}");
        }

        if (group.Tags.Count > 10)
        {
            Console.WriteLine($"  ... and {group.Tags.Count - 10} more tags");
        }
        Console.WriteLine();
    }

    Console.WriteLine($"Tag list saved to: {filePath}");
}

static async Task SyncCacheAsync(
    string connectionString,
    string databaseType,
    string cosmosDatabaseName,
    string cacheDir,
    int safeWindowMinutes,
    bool verbose)
{
    Console.WriteLine("=== Cache Sync ===\n");
    Console.WriteLine($"Remote Database: {databaseType}");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine($"Safe Window: {safeWindowMinutes} minutes");
    Console.WriteLine();

    Directory.CreateDirectory(cacheDir);
    var cachePath = Path.Combine(cacheDir, "events.db");

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var remoteStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    var localStore = SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath, domainTypes);

    var syncOptions = new CacheSyncOptions
    {
        SafeWindow = TimeSpan.FromMinutes(safeWindowMinutes),
        RemoteEndpoint = connectionString.Length > 50 ? connectionString[..50] + "..." : connectionString,
        DatabaseName = databaseType.ToLowerInvariant() == "cosmos" ? cosmosDatabaseName : "postgres"
    };
    var cacheSync = SekibanDcbSqliteExtensions.CreateCacheSync(localStore, remoteStore, syncOptions);

    Console.WriteLine("Syncing events from remote to local cache...\n");

    var result = await cacheSync.SyncAsync();
    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.ErrorMessage}");
        return;
    }

    Console.WriteLine("Sync completed:");
    Console.WriteLine($"  Action: {result.Action}");
    Console.WriteLine($"  Events synced: {result.EventsSynced:N0}");
    Console.WriteLine($"  Total events in cache: {result.TotalEventsInCache:N0}");
    Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
    Console.WriteLine($"\nCache file: {cachePath}");
}

static async Task ShowCacheStatsAsync(string cacheDir)
{
    Console.WriteLine("=== Cache Statistics ===\n");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine();

    var cachePath = Path.Combine(cacheDir, "events.db");

    if (!File.Exists(cachePath))
    {
        Console.WriteLine("No cache file found.");
        Console.WriteLine($"Expected path: {cachePath}");
        return;
    }

    var fileInfo = new FileInfo(cachePath);
    Console.WriteLine($"Cache File: {cachePath}");
    Console.WriteLine($"File Size: {FormatBytes(fileInfo.Length)}");
    Console.WriteLine($"Last Modified: {fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine();

    var domainTypes = DomainType.GetDomainTypes();
    var localStore = SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath, domainTypes);

    var countResult = await localStore.GetEventCountAsync();
    if (countResult.IsSuccess)
    {
        Console.WriteLine($"Total Events: {countResult.GetValue():N0}");
    }

    var metadata = await localStore.GetMetadataAsync();
    if (metadata != null)
    {
        Console.WriteLine();
        Console.WriteLine("Cache Metadata:");
        Console.WriteLine($"  Remote Endpoint: {metadata.RemoteEndpoint}");
        Console.WriteLine($"  Database Name: {metadata.DatabaseName}");
        Console.WriteLine($"  Last Sync: {metadata.UpdatedUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Last SortableUniqueId: {metadata.LastCachedSortableUniqueId}");
        Console.WriteLine($"  Schema Version: {metadata.SchemaVersion}");
    }
}

static async Task ClearCacheAsync(string cacheDir)
{
    Console.WriteLine("=== Clear Cache ===\n");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine();

    var cachePath = Path.Combine(cacheDir, "events.db");

    if (!File.Exists(cachePath))
    {
        Console.WriteLine("No cache file found. Nothing to clear.");
        return;
    }

    var fileInfo = new FileInfo(cachePath);
    Console.WriteLine($"Cache file: {cachePath}");
    Console.WriteLine($"File size: {FormatBytes(fileInfo.Length)}");
    Console.WriteLine();

    Console.Write("Are you sure you want to delete this cache? (y/N): ");
    var response = Console.ReadLine();
    if (response?.ToLowerInvariant() != "y")
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    try
    {
        DeleteCacheFiles(cachePath);
        Console.WriteLine("\nCache cleared successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError deleting cache: {ex.Message}");
    }

    await Task.CompletedTask;
}

enum SimpleCacheMode
{
    Auto,
    Off,
    Clear,
    CacheOnly
}

readonly record struct CacheOptions(SimpleCacheMode Mode, string CacheDir);
