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
using Sekiban.Dcb.Services;
using Sekiban.Dcb.Storage;

// Build configuration from environment variables and user secrets
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

// Create the root command
var rootCommand = new RootCommand("Sekiban DCB CLI - Manage projections, tags, and events");

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

// Save command - export state JSON to file for debugging
var outputDirOption = new Option<string?>(
    name: "--output-dir",
    getDefaultValue: () => configuration["OUTPUT_DIR"] ?? "./output",
    description: "Output directory for saved state files");
outputDirOption.AddAlias("-o");

var saveCommand = new Command("save", "Save projection state JSON to files for debugging")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption,
    outputDirOption
};

saveCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await SaveStateJsonAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName, outputDir);
});

// Delete command - delete projection states
var deleteCommand = new Command("delete", "Delete projection states")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

deleteCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

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
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    tagOption,
    outputDirOption
};

tagEventsCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var tag = context.ParseResult.GetValueForOption(tagOption) ?? "";
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await FetchTagEventsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, outputDir);
});

// Projection command - display current projection state
var projectionCommand = new Command("projection", "Display the current state of a projection")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    projectorNameOption
};

projectionCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var projectorName = context.ParseResult.GetValueForOption(projectorNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowProjectionAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, projectorName);
});

// Tag-state command - project events for a tag using a specific projector
var tagProjectorOption = new Option<string>(
    name: "--projector",
    description: "Tag projector name to use for projection (e.g., 'WeatherForecastProjector')")
{
    IsRequired = true
};
tagProjectorOption.AddAlias("-P");

var tagStateCommand = new Command("tag-state", "Project and display the current state for a specific tag")
{
    databaseOption,
    connectionStringOption,
    cosmosConnectionStringOption,
    cosmosDatabaseNameOption,
    tagOption,
    tagProjectorOption
};

tagStateCommand.SetHandler(async (context) =>
{
    var database = context.ParseResult.GetValueForOption(databaseOption);
    var connectionString = context.ParseResult.GetValueForOption(connectionStringOption);
    var cosmosConnectionString = context.ParseResult.GetValueForOption(cosmosConnectionStringOption);
    var cosmosDatabaseName = context.ParseResult.GetValueForOption(cosmosDatabaseNameOption);
    var tag = context.ParseResult.GetValueForOption(tagOption) ?? "";
    var tagProjector = context.ParseResult.GetValueForOption(tagProjectorOption) ?? "";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowTagStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, tagProjector);
});

rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(statusCommand);
rootCommand.AddCommand(saveCommand);
rootCommand.AddCommand(deleteCommand);
rootCommand.AddCommand(tagEventsCommand);
rootCommand.AddCommand(projectionCommand);
rootCommand.AddCommand(tagStateCommand);

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

    // Prepare events for JSON serialization
    var eventsForJson = events.Select(e => new
    {
        e.Id,
        SortableUniqueId = e.SortableUniqueIdValue,
        e.EventType,
        e.Tags,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType(), domainTypes.JsonSerializerOptions),
        e.Payload
    }).ToList();

    // Save to file
    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var safeTagName = tagString.Replace(":", "_").Replace("/", "_");
    var fileName = $"tag_events_{safeTagName}_{timestamp}.json";
    var filePath = Path.Combine(outputDir, fileName);

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true
    };

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

static async Task ShowTagStateAsync(string connectionString, string databaseType, string cosmosDatabaseName, string tagString, string projectorName)
{
    Console.WriteLine("=== Tag State Projection ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine($"Projector: {projectorName}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var tagStateService = services.GetRequiredService<TagStateService>();

    // Show available projectors if not specified
    if (string.IsNullOrEmpty(projectorName))
    {
        Console.WriteLine("Available Tag Projectors:");
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

    // Project the tag state
    Console.WriteLine("Projecting events...\n");
    var result = await tagStateService.ProjectTagStateAsync(tagString, projectorName);

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

    // Register CLI services
    services.AddSingleton<TagEventService>();
    services.AddSingleton<TagStateService>();

    return services.BuildServiceProvider();
}
