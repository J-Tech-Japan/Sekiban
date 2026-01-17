using System.CommandLine;
using System.Reflection;
using Dcb.Domain;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Sqlite.Services;
using Sekiban.Dcb.Storage;
using System.Text.Encodings.Web;

// Build configuration from environment variables and user secrets
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

/*
User secrets configuration example (dotnet user-secrets set):
{
    "Profiles": {
        "dev": {
            "Database": "postgres",
            "ConnectionString": "Host=localhost;Database=sekiban;Username=...",
            "CosmosConnectionString": "AccountEndpoint=...",
            "CosmosDatabase": "SekibanDcb"
        },
        "stg": {
            "Database": "cosmos",
            "CosmosConnectionString": "AccountEndpoint=...",
            "CosmosDatabase": "SekibanDcbStaging"
        }
    },
    "DefaultProfile": "dev"
}

Usage:
  dotnet run -- status --profile dev
  dotnet run -- build -f --profile stg
  dotnet run -- profiles
*/

// Create the root command
var rootCommand = new RootCommand("Sekiban DCB CLI - Manage projections, tags, and events");

// Profile option (shared across commands)
var profileOption = new Option<string?>("--profile", "-P")
{
    Description = "Profile name defined in user secrets (e.g., dev, stg, prod)"
};

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
    profileOption,
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
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var minEvents = context.ParseResult.GetValueForOption(minEventsOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);
    var forceRebuild = context.ParseResult.GetValueForOption(forceRebuildOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

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
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption
};

statusCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await ShowStatusAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName);
});

// Save command - export state JSON to file for debugging
var outputDirOption = new Option<string?>(
    name: "--output-dir",
    getDefaultValue: () => configuration["OUTPUT_DIR"] ?? "./output",
    description: "Output directory for saved state files");
outputDirOption.AddAlias("-o");

var saveCommand = new Command("save", "Save projection state JSON to files for debugging")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption,
    outputDirOption
};

saveCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption) ?? "./output";

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await SaveStateJsonAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName, outputDir);
});

// Delete command - delete projection states
var deleteCommand = new Command("delete", "Delete projection states")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

deleteCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await DeleteStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName);
});

// Tag-events command - fetch events for a specific tag
var tagOption = new Option<string>(
    name: "--tag",
    description: "Tag string in format 'group:content' (e.g., 'WeatherForecast:00000000-0000-0000-0000-000000000001')")
{
    IsRequired = true
};
tagOption.AddAlias("-t");

var tagEventsCommand = new Command("tag-events", "Fetch and save all events for a specific tag")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    tagOption,
    outputDirOption
};

tagEventsCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var tag = context.ParseResult.GetValueForOption(tagOption) ?? "";
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption) ?? "./output";

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await FetchTagEventsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, outputDir);
});

// Projection command - display current projection state
var projectionCommand = new Command("projection", "Display the current state of a projection")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

projectionCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await ShowProjectionAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName);
});

// Tag-state command - project events for a tag using a specific projector
var tagProjectorOption = new Option<string?>(
    name: "--projector",
    description: "Tag projector name to use for projection. Auto-detects if not specified (e.g., 'UserMonthlyReservation' tag uses 'UserMonthlyReservationProjector')");
tagProjectorOption.AddAlias("-P");

var tagStateCommand = new Command("tag-state", "Project and display the current state for a specific tag")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    tagOption,
    tagProjectorOption
};

tagStateCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var tag = context.ParseResult.GetValueForOption(tagOption) ?? "";
    var tagProjector = context.ParseResult.GetValueForOption(tagProjectorOption) ?? "";

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await ShowTagStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, tagProjector);
});

// Tag-list command - list all tags in the event store
var tagGroupOption = new Option<string?>(
    name: "--tag-group",
    description: "Filter by tag group name (e.g., 'WeatherForecast')");
tagGroupOption.AddAlias("-g");

var tagListCommand = new Command("tag-list", "List all tags in the event store and export to JSON")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    tagGroupOption,
    outputDirOption
};

tagListCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var tagGroup = context.ParseResult.GetValueForOption(tagGroupOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption) ?? "./output";

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await ListTagsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tagGroup, outputDir);
});

// Cache directory option
var cacheDirOption = new Option<string>(
    name: "--cache-dir",
    getDefaultValue: () => configuration["CACHE_DIR"] ?? "./cache",
    description: "Directory for local SQLite cache");
cacheDirOption.AddAlias("-C");

// Safe window option (minutes)
var safeWindowOption = new Option<int>(
    name: "--safe-window",
    getDefaultValue: () => int.TryParse(configuration["SAFE_WINDOW_MINUTES"], out var val) ? val : 10,
    description: "Safe window in minutes (events within this window are not cached)");

// Cache-sync command - sync remote events to local SQLite cache
var cacheSyncCommand = new Command("cache-sync", "Sync remote events to local SQLite cache")
{
    profileOption,
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    cacheDirOption,
    safeWindowOption,
    verboseOption
};

cacheSyncCommand.SetHandler(async (context) =>
{
    var profile = context.ParseResult.GetValueForOption(profileOption);
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var cacheDir = context.ParseResult.GetValueForOption(cacheDirOption);
    var safeWindowMinutes = context.ParseResult.GetValueForOption(safeWindowOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);

    var profileConfig = ResolveProfile(configuration, profile);
    if (profileConfig == null) return;

    var resolvedDatabase = ResolveDatabaseType(profileConfig, database);
    var resolvedConnectionString = ResolveConnectionString(profileConfig, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, cosmosDatabaseName);

    await SyncCacheAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, cacheDir ?? "./cache", safeWindowMinutes, verbose);
});

// Cache-stats command - show cache statistics
var cacheStatsCommand = new Command("cache-stats", "Show local cache statistics")
{
    cacheDirOption
};

cacheStatsCommand.SetHandler(async (context) =>
{
    var cacheDir = context.ParseResult.GetValueForOption(cacheDirOption) ?? "./cache";
    await ShowCacheStatsAsync(cacheDir);
});

// Cache-clear command - clear local cache
var cacheClearCommand = new Command("cache-clear", "Clear local SQLite cache")
{
    cacheDirOption
};

cacheClearCommand.SetHandler(async (context) =>
{
    var cacheDir = context.ParseResult.GetValueForOption(cacheDirOption) ?? "./cache";
    await ClearCacheAsync(cacheDir);
});

// Profiles command - list available profiles
var profilesCommand = new Command("profiles", "List available profiles defined in user secrets");
profilesCommand.SetHandler(() =>
{
    ListProfiles(configuration);
});

rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(statusCommand);
rootCommand.AddCommand(saveCommand);
rootCommand.AddCommand(deleteCommand);
rootCommand.AddCommand(tagEventsCommand);
rootCommand.AddCommand(projectionCommand);
rootCommand.AddCommand(tagStateCommand);
rootCommand.AddCommand(tagListCommand);
rootCommand.AddCommand(cacheSyncCommand);
rootCommand.AddCommand(cacheStatsCommand);
rootCommand.AddCommand(cacheClearCommand);
rootCommand.AddCommand(profilesCommand);

return await rootCommand.InvokeAsync(args);

// Helper to resolve profile configuration
static IConfiguration? ResolveProfile(IConfiguration configuration, string? profileName)
{
    var profilesSection = configuration.GetSection("Profiles");
    var profileEntries = profilesSection.GetChildren().ToList();
    var hasProfiles = profileEntries.Count > 0;

    var resolvedProfile = profileName;
    if (string.IsNullOrWhiteSpace(resolvedProfile))
    {
        var defaultProfile = configuration["DefaultProfile"];
        if (!string.IsNullOrWhiteSpace(defaultProfile))
        {
            resolvedProfile = defaultProfile;
        }
        else if (hasProfiles)
        {
            Console.WriteLine("Error: No profile specified. Use --profile option.");
            Console.WriteLine($"Available profiles: {string.Join(", ", profileEntries.Select(p => p.Key))}");
            return null;
        }
        else
        {
            // No profiles defined, use root configuration
            return configuration;
        }
    }

    if (!hasProfiles)
    {
        Console.WriteLine($"Error: Profile '{resolvedProfile}' is not defined. Add it to Profiles in User Secrets.");
        return null;
    }

    var profileSection = profilesSection.GetSection(resolvedProfile!);
    if (!profileSection.Exists())
    {
        Console.WriteLine($"Error: Profile '{resolvedProfile}' not found.");
        Console.WriteLine($"Available profiles: {string.Join(", ", profileEntries.Select(p => p.Key))}");
        return null;
    }

    var prefix = $"Profiles:{resolvedProfile}:";
    var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var pair in profileSection.AsEnumerable())
    {
        if (pair.Value == null)
        {
            continue;
        }

        var key = pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pair.Key[prefix.Length..]
            : pair.Key;

        overrides[key] = pair.Value;
    }

    // Map alias keys to standard config keys
    void MapAlias(string aliasKey, string targetKey)
    {
        if (overrides.TryGetValue(aliasKey, out var value) && !overrides.ContainsKey(targetKey))
        {
            overrides[targetKey] = value;
        }
    }

    MapAlias("Database", "Sekiban:Database");
    MapAlias("CosmosConnectionString", "ConnectionStrings:SekibanDcbCosmos");
    MapAlias("CosmosDatabase", "CosmosDb:DatabaseName");
    MapAlias("ConnectionString", "ConnectionStrings:DcbPostgres");
    MapAlias("PostgresConnectionString", "ConnectionStrings:DcbPostgres");

    return new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(overrides!)
        .Build();
}

// Helper to list profiles
static void ListProfiles(IConfiguration configuration)
{
    var profiles = configuration.GetSection("Profiles").GetChildren().ToList();
    if (profiles.Count == 0)
    {
        Console.WriteLine("No profiles found.");
        Console.WriteLine("\nTo add profiles, use user secrets:");
        Console.WriteLine("  dotnet user-secrets set \"Profiles:dev:Database\" \"postgres\"");
        Console.WriteLine("  dotnet user-secrets set \"Profiles:dev:ConnectionString\" \"Host=localhost;...\"");
        Console.WriteLine("  dotnet user-secrets set \"DefaultProfile\" \"dev\"");
        return;
    }

    var defaultProfile = configuration["DefaultProfile"];
    Console.WriteLine("=== Available Profiles ===\n");
    foreach (var profile in profiles)
    {
        var isDefault = !string.IsNullOrWhiteSpace(defaultProfile) &&
                        string.Equals(profile.Key, defaultProfile, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(isDefault ? $"  - {profile.Key} (default)" : $"  - {profile.Key}");
    }
}

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

static async Task SaveStateJsonAsync(string connectionString, string databaseType, string cosmosDatabaseName, string? projectorName, string outputDir)
{
    Console.WriteLine("=== Save Projection State JSON ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();

    // Create output directory
    Directory.CreateDirectory(outputDir);

    // Get list of states (summary info)
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

        // Fetch the full record with state data
        var recordResult = await stateStore.GetLatestForVersionAsync(info.ProjectorName, info.ProjectorVersion);
        if (!recordResult.IsSuccess || !recordResult.GetValue().HasValue)
        {
            Console.WriteLine($"  Error: Could not fetch full record");
            continue;
        }

        var state = recordResult.GetValue().GetValue();

        // Save metadata
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

        // Save decompressed state data
        if (state.StateData != null)
        {
            var stateFileName = $"{state.ProjectorName}_{timestamp}_state.json";
            var statePath = Path.Combine(outputDir, stateFileName);

            try
            {
                // v10 format: StateData is plain JSON (UTF-8), not Gzip compressed at outer level
                // The inner payload may be compressed via custom serializer
                string jsonContent;

                // Auto-detect: Gzip (v9) or plain JSON (v10)
                if (state.StateData.Length >= 2 && state.StateData[0] == 0x1f && state.StateData[1] == 0x8b)
                {
                    // v9 format: Gzip compressed
                    jsonContent = GzipCompression.DecompressToString(state.StateData);
                }
                else
                {
                    // v10 format: Plain UTF-8 JSON
                    jsonContent = System.Text.Encoding.UTF8.GetString(state.StateData);
                }

                // Pretty print the JSON
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var prettyJson = System.Text.Json.JsonSerializer.Serialize(jsonDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(statePath, prettyJson);
                Console.WriteLine($"  State: {stateFileName} ({FormatBytes(state.StateData.LongLength)})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error decompressing state: {ex.Message}");
                // Save raw bytes as fallback
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

    // Get states to delete
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

static async Task FetchTagEventsAsync(string connectionString, string databaseType, string cosmosDatabaseName, string tagString, string outputDir)
{
    Console.WriteLine("=== Fetch Tag Events ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var eventStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Parse the tag string
    var tag = domainTypes.TagTypes.GetTag(tagString);
    Console.WriteLine($"Parsed Tag: {tag.GetType().Name} = {tag}");
    Console.WriteLine();

    // Fetch events for the tag
    Console.WriteLine("Fetching events...");
    var eventsResult = await eventStore.ReadEventsByTagAsync(tag);
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

    // Create output directory
    Directory.CreateDirectory(outputDir);

    // JSON options with proper UTF-8 encoding
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Prepare events for JSON serialization
    var eventsForJson = events.Select(e =>
    {
        var payloadJsonString = System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType(), jsonOptions);
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

    // Save to file
    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var safeTagName = tagString.Replace(":", "_").Replace("/", "_");
    var fileName = $"tag_events_{safeTagName}_{timestamp}.json";
    var filePath = Path.Combine(outputDir, fileName);

    var output = new
    {
        Tag = tagString,
        EventCount = events.Count,
        FetchedAt = DateTime.UtcNow,
        Events = eventsForJson
    };

    await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(output, jsonOptions));

    Console.WriteLine($"Events saved to: {filePath}");

    // Print summary of events
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

    // Get list of states
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

        // Show available projectors
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

        // Fetch the full record to show state details
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
                // Try to deserialize and show a preview of the state
                string jsonContent;

                // Auto-detect format
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

                // Try to extract useful information from the state
                if (jsonDoc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var itemCount = items.GetArrayLength();
                    Console.WriteLine($"    Items Count: {itemCount}");

                    // Show first few items
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
                    // Show root properties
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

static async Task ShowTagStateAsync(string connectionString, string databaseType, string cosmosDatabaseName, string tagString, string? projectorName)
{
    Console.WriteLine("=== Tag State Projection ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var tagStateService = services.GetRequiredService<TagStateService>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Auto-detect projector name from tag group if not specified
    var resolvedProjectorName = projectorName;
    if (string.IsNullOrEmpty(resolvedProjectorName))
    {
        // Extract tag group from tag string (e.g., "UserMonthlyReservation:xxx" -> "UserMonthlyReservation")
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

    // Project the tag state
    Console.WriteLine("Projecting events...\n");
    var result = await tagStateService.ProjectTagStateAsync(tagString, resolvedProjectorName);

    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.GetException().Message}");

        // Show available projectors
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

    // Show the state
    Console.WriteLine("Projected State:");
    Console.WriteLine(new string('-', 60));

    var stateType = projectionResult.State.GetType();
    Console.WriteLine($"Type: {stateType.Name}");

    // Serialize the state to JSON for display
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true
    };
    var stateJson = System.Text.Json.JsonSerializer.Serialize(projectionResult.State, stateType, jsonOptions);
    Console.WriteLine(stateJson);
}

static ServiceProvider BuildServices(string connectionString, string databaseType, string cosmosDatabaseName, bool enableProgressReporting = true)
{
    var services = new ServiceCollection();

    // Register domain types
    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        // Use default optimized options (Direct mode, large page size, parallel deserialization)
        var cosmosOptions = new CosmosDbEventStoreOptions();

        // Add progress callback if enabled
        if (enableProgressReporting)
        {
            var lastReportTime = DateTime.UtcNow;
            cosmosOptions.ReadProgressCallback = (eventsRead, ruConsumed) =>
            {
                // Report progress every 2 seconds to avoid too much console output
                var now = DateTime.UtcNow;
                if ((now - lastReportTime).TotalSeconds >= 2)
                {
                    Console.WriteLine($"  Progress: {eventsRead:N0} events read, {ruConsumed:N2} RU consumed");
                    lastReportTime = now;
                }
            };
        }

        // Register Cosmos DB services with options
        var cosmosContext = new CosmosDbContext(connectionString, cosmosDatabaseName, options: cosmosOptions);
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

    // Register CLI services
    services.AddSingleton<TagEventService>();
    services.AddSingleton<TagStateService>();
    services.AddSingleton<TagListService>();

    return services.BuildServiceProvider();
}

static async Task ListTagsAsync(string connectionString, string databaseType, string cosmosDatabaseName, string? tagGroup, string outputDir)
{
    Console.WriteLine("=== Tag List ===\n");
    Console.WriteLine($"Database: {databaseType}");
    if (!string.IsNullOrEmpty(tagGroup))
    {
        Console.WriteLine($"Filter: {tagGroup}");
    }
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var tagListService = services.GetRequiredService<TagListService>();

    // Export tag list
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

    // Display tag groups
    foreach (var group in exportResult.TagGroups)
    {
        Console.WriteLine($"[{group.TagGroup}] ({group.TagCount} tags, {group.TotalEvents:N0} events)");

        // Show first 10 tags
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

static async Task SyncCacheAsync(string connectionString, string databaseType, string cosmosDatabaseName, string cacheDir, int safeWindowMinutes, bool verbose)
{
    Console.WriteLine("=== Cache Sync ===\n");
    Console.WriteLine($"Remote Database: {databaseType}");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine($"Safe Window: {safeWindowMinutes} minutes");
    Console.WriteLine();

    // Create cache directory
    Directory.CreateDirectory(cacheDir);
    var cachePath = Path.Combine(cacheDir, "events.db");

    // Build remote services
    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var remoteStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Create local SQLite cache
    var localStore = SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath, domainTypes);

    // Create cache sync helper
    var syncOptions = new CacheSyncOptions
    {
        SafeWindow = TimeSpan.FromMinutes(safeWindowMinutes),
        RemoteEndpoint = connectionString.Length > 50 ? connectionString[..50] + "..." : connectionString,
        DatabaseName = databaseType.ToLowerInvariant() == "cosmos" ? cosmosDatabaseName : "postgres"
    };
    var cacheSync = SekibanDcbSqliteExtensions.CreateCacheSync(localStore, remoteStore, syncOptions);

    // Perform sync
    Console.WriteLine("Syncing events from remote to local cache...\n");

    var result = await cacheSync.SyncAsync();
    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.ErrorMessage}");
        return;
    }

    Console.WriteLine($"Sync completed:");
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

    // Get file info
    var fileInfo = new FileInfo(cachePath);
    Console.WriteLine($"Cache File: {cachePath}");
    Console.WriteLine($"File Size: {FormatBytes(fileInfo.Length)}");
    Console.WriteLine($"Last Modified: {fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine();

    // Create a minimal SQLite store to read stats
    var domainTypes = DomainType.GetDomainTypes();
    var localStore = SekibanDcbSqliteExtensions.CreateSqliteCache(cachePath, domainTypes);

    // Get event count
    var countResult = await localStore.GetEventCountAsync();
    if (countResult.IsSuccess)
    {
        Console.WriteLine($"Total Events: {countResult.GetValue():N0}");
    }

    // Get metadata
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

    // Get tag summary
    var tagsResult = await localStore.GetAllTagsAsync();
    if (tagsResult.IsSuccess)
    {
        var tags = tagsResult.GetValue().ToList();
        var tagGroups = tags.GroupBy(t => t.TagGroup).ToList();

        Console.WriteLine();
        Console.WriteLine($"Tags: {tags.Count} total across {tagGroups.Count} groups");

        foreach (var group in tagGroups.Take(5))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()} tags");
        }

        if (tagGroups.Count > 5)
        {
            Console.WriteLine($"  ... and {tagGroups.Count - 5} more groups");
        }
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

    // Get file info before deletion
    var fileInfo = new FileInfo(cachePath);
    var fileSize = fileInfo.Length;

    Console.WriteLine($"Cache file: {cachePath}");
    Console.WriteLine($"File size: {FormatBytes(fileSize)}");
    Console.WriteLine();

    Console.Write("Are you sure you want to delete this cache? (y/N): ");
    var response = Console.ReadLine();
    if (response?.ToLowerInvariant() != "y")
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    // Delete the cache file and WAL/SHM files
    try
    {
        File.Delete(cachePath);

        var walPath = cachePath + "-wal";
        var shmPath = cachePath + "-shm";

        if (File.Exists(walPath))
            File.Delete(walPath);
        if (File.Exists(shmPath))
            File.Delete(shmPath);

        Console.WriteLine("\nCache cleared successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError deleting cache: {ex.Message}");
    }

    await Task.CompletedTask;
}
