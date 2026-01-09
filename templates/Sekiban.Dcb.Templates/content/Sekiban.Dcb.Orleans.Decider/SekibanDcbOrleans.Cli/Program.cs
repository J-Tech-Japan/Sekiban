using System.CommandLine;
using System.Reflection;
using Dcb.EventSource;
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
var rootCommand = new RootCommand(
@"Sekiban DCB CLI - Manage projections, tags, and events

Configuration:
  Connection strings can be configured via:

  1. Command line options:
     -c, --connection-string ""Host=localhost;Database=mydb;...""

  2. Environment variables:
     export CONNECTION_STRING=""Host=localhost;Database=mydb;...""
     export DATABASE_TYPE=""postgres""  # or ""cosmos""
     export COSMOS_CONNECTION_STRING=""AccountEndpoint=...""
     export COSMOS_DATABASE_NAME=""SekibanDcb""
     export OUTPUT_DIR=""./output""

  3. User Secrets (recommended for development):
     dotnet user-secrets init  # if not already initialized
     dotnet user-secrets set ""ConnectionStrings:DcbPostgres"" ""Host=localhost;Database=mydb;...""
     dotnet user-secrets set ""ConnectionStrings:SekibanDcbCosmos"" ""AccountEndpoint=...""
     dotnet user-secrets set ""Sekiban:Database"" ""postgres""
     dotnet user-secrets set ""CosmosDb:DatabaseName"" ""SekibanDcb""

Examples:
  # List all registered projectors and tag groups
  dotnet run -- list

  # Show status (with connection string configured via env/secrets)
  dotnet run -- status

  # Show status with explicit connection string
  dotnet run -- status -c ""Host=localhost;Database=mydb;Username=user;Password=pass""

  # Build all projection states
  dotnet run -- build

  # Fetch events for a specific tag
  dotnet run -- tag-events -t ""WeatherForecast:00000000-0000-0000-0000-000000000001""

  # Project tag state (auto-detects projector from tag group: WeatherForecast -> WeatherForecastProjector)
  dotnet run -- tag-state -t ""WeatherForecast:guid-here""

  # Project tag state with explicit projector
  dotnet run -- tag-state -t ""WeatherForecast:guid-here"" -P ""WeatherForecastProjector""

  # Save projection state to JSON files (defaults to ./output)
  dotnet run -- save

  # List all tags in the event store (saves to ./output/tag_list_{timestamp}.json)
  dotnet run -- tag-list

  # List tags filtered by group
  dotnet run -- tag-list -g ""WeatherForecast""

  # Use Cosmos DB instead of PostgreSQL
  dotnet run -- status -d cosmos");

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

// Tag projector option
var tagProjectorOption = new Option<string?>("--tag-projector", "-P")
{
    Description = "Tag projector name to use for projection. If not specified, tries '{TagGroupName}Projector' convention."
};

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

buildCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var minEvents = parseResult.GetValue(minEventsOption);
    var projectorName = parseResult.GetValue(projectorNameOption);
    var forceRebuild = parseResult.GetValue(forceRebuildOption);
    var verbose = parseResult.GetValue(verboseOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await BuildProjectionStatesAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, minEvents, projectorName, forceRebuild, verbose);
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
    cosmosDatabaseNameOption
};

statusCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowStatusAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName);
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
    tagOption,
    outputDirOption
};

tagEventsCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var tag = parseResult.GetValue(tagOption) ?? "";
    var outputDir = parseResult.GetValue(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await FetchTagEventsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, outputDir);
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
    tagOption,
    tagProjectorOption
};

tagStateCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var tag = parseResult.GetValue(tagOption) ?? "";
    var tagProjector = parseResult.GetValue(tagProjectorOption) ?? "";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ShowTagStateAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tag, tagProjector);
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
    tagGroupOption,
    outputDirOption
};

tagListCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var database = parseResult.GetValue(databaseOption);
    var connectionString = parseResult.GetValue(connectionStringOption);
    var cosmosConnectionString = parseResult.GetValue(cosmosConnectionStringOption);
    var cosmosDatabaseName = parseResult.GetValue(cosmosDatabaseNameOption);
    var tagGroup = parseResult.GetValue(tagGroupOption);
    var outputDir = parseResult.GetValue(outputDirOption) ?? "./output";

    var resolvedDatabase = ResolveDatabaseType(configuration, database);
    var resolvedConnectionString = ResolveConnectionString(configuration, resolvedDatabase, connectionString, cosmosConnectionString);
    var resolvedCosmosDatabaseName = ResolveCosmosDatabaseName(configuration, cosmosDatabaseName);

    await ListTagsAsync(resolvedConnectionString, resolvedDatabase, resolvedCosmosDatabaseName, tagGroup, outputDir);
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

static async Task FetchTagEventsAsync(string connectionString, string databaseType, string cosmosDatabaseName, string tagString, string outputDir)
{
    Console.WriteLine("=== Fetch Tag Events ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
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

    var eventsForJson = events.Select(e => new
    {
        e.Id,
        SortableUniqueId = e.SortableUniqueIdValue,
        e.EventType,
        e.Tags,
        PayloadJson = System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType(), tagEventService.JsonSerializerOptions),
        e.Payload
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

static async Task ShowTagStateAsync(string connectionString, string databaseType, string cosmosDatabaseName, string tagString, string? projectorName)
{
    Console.WriteLine("=== Tag State Projection ===\n");
    Console.WriteLine($"Database: {databaseType}");
    Console.WriteLine($"Tag: {tagString}");

    var services = BuildServices(connectionString, databaseType, cosmosDatabaseName);
    var tagStateService = services.GetRequiredService<TagStateService>();

    // Use auto-inference if projector name is not specified
    var effectiveProjectorName = projectorName;
    if (string.IsNullOrEmpty(effectiveProjectorName))
    {
        // Extract tag group from tag string (format: "group:content")
        var colonIndex = tagString.IndexOf(':');
        if (colonIndex > 0)
        {
            var tagGroup = tagString[..colonIndex];
            var conventionName = $"{tagGroup}Projector";

            // Check if this projector exists
            var availableProjectors = tagStateService.GetAllTagProjectorNames();
            effectiveProjectorName = availableProjectors.FirstOrDefault(p =>
                p.Equals(conventionName, StringComparison.OrdinalIgnoreCase));

            if (effectiveProjectorName == null)
            {
                // Try to find any projector starting with the tag group name
                effectiveProjectorName = availableProjectors.FirstOrDefault(p =>
                    p.StartsWith(tagGroup, StringComparison.OrdinalIgnoreCase));
            }

            if (effectiveProjectorName != null)
            {
                Console.WriteLine($"Projector: {effectiveProjectorName} (auto-detected from tag group '{tagGroup}')");
            }
            else
            {
                Console.WriteLine($"Error: Could not find a projector for tag group '{tagGroup}'.");
                Console.WriteLine($"Tried: '{conventionName}'");
                Console.WriteLine("\nAvailable Tag Projectors:");
                foreach (var name in availableProjectors)
                {
                    Console.WriteLine($"  - {name}");
                }
                return;
            }
        }
        else
        {
            Console.WriteLine("Error: Invalid tag format. Expected 'group:content'.");
            return;
        }
    }
    else
    {
        Console.WriteLine($"Projector: {effectiveProjectorName}");
    }
    Console.WriteLine();

    Console.WriteLine("Projecting events...\n");
    var result = await tagStateService.ProjectTagStateAsync(tagString, effectiveProjectorName);

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

static ServiceProvider BuildServices(string connectionString, string databaseType, string cosmosDatabaseName)
{
    var services = new ServiceCollection();

    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        var cosmosClient = new CosmosClient(connectionString);
        var cosmosContext = new CosmosDbContext(cosmosClient, cosmosDatabaseName);
        services.AddSingleton(cosmosContext);
        services.AddSingleton<IEventStore, CosmosDbEventStore>();
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();
    }
    else
    {
        services.AddPooledDbContextFactory<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddSingleton<IEventStore, PostgresEventStore>();
        services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();
    }

    services.AddSingleton<MultiProjectionStateBuilder>();
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
