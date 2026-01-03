using System.CommandLine;
using Dcb.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;

// Create the root command
var rootCommand = new RootCommand("Multi Projection State Builder CLI - Pre-build projection states for faster startup");

// Connection string option (optional - can be read from environment variable)
var connectionStringOption = new Option<string?>(
    name: "--connection-string",
    description: "PostgreSQL connection string (defaults to ConnectionStrings__DcbPostgres env var)");
connectionStringOption.AddAlias("-c");

// Min events option
var minEventsOption = new Option<int>(
    name: "--min-events",
    getDefaultValue: () => int.TryParse(Environment.GetEnvironmentVariable("MIN_EVENTS"), out var val) ? val : 3000,
    description: "Minimum events required before building state");
minEventsOption.AddAlias("-m");

// Projector name option (optional - if not specified, build all)
var projectorNameOption = new Option<string?>(
    name: "--projector",
    getDefaultValue: () => Environment.GetEnvironmentVariable("PROJECTOR_NAME"),
    description: "Specific projector name to build (if not specified, builds all)");
projectorNameOption.AddAlias("-p");

// Force rebuild option
var forceRebuildOption = new Option<bool>(
    name: "--force",
    getDefaultValue: () => Environment.GetEnvironmentVariable("FORCE_REBUILD")?.ToLowerInvariant() == "true",
    description: "Force rebuild even if state exists");
forceRebuildOption.AddAlias("-f");

// Verbose option
var verboseOption = new Option<bool>(
    name: "--verbose",
    getDefaultValue: () => Environment.GetEnvironmentVariable("VERBOSE")?.ToLowerInvariant() == "true",
    description: "Show verbose output");
verboseOption.AddAlias("-v");

// Build command
var buildCommand = new Command("build", "Build multi projection states")
{
    connectionStringOption,
    minEventsOption,
    projectorNameOption,
    forceRebuildOption,
    verboseOption
};

buildCommand.SetHandler(async (connectionString, minEvents, projectorName, forceRebuild, verbose) =>
{
    var resolvedConnectionString = ResolveConnectionString(connectionString);
    await BuildProjectionStatesAsync(resolvedConnectionString, minEvents, projectorName, forceRebuild, verbose);
}, connectionStringOption, minEventsOption, projectorNameOption, forceRebuildOption, verboseOption);

// List command
var listCommand = new Command("list", "List all registered projectors")
{
    connectionStringOption
};

listCommand.SetHandler(async (connectionString) =>
{
    await ListProjectorsAsync();
}, connectionStringOption);

// Status command
var statusCommand = new Command("status", "Show status of all projection states")
{
    connectionStringOption
};

statusCommand.SetHandler(async (connectionString) =>
{
    var resolvedConnectionString = ResolveConnectionString(connectionString);
    await ShowStatusAsync(resolvedConnectionString);
}, connectionStringOption);

rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(statusCommand);

return await rootCommand.InvokeAsync(args);

// Helper to resolve connection string from argument or environment
static string ResolveConnectionString(string? connectionString)
{
    if (!string.IsNullOrEmpty(connectionString))
        return connectionString;

    // Try Aspire-style connection string
    var aspireConnStr = Environment.GetEnvironmentVariable("ConnectionStrings__DcbPostgres");
    if (!string.IsNullOrEmpty(aspireConnStr))
        return aspireConnStr;

    // Try standard connection string environment variable
    var standardConnStr = Environment.GetEnvironmentVariable("CONNECTION_STRING");
    if (!string.IsNullOrEmpty(standardConnStr))
        return standardConnStr;

    throw new InvalidOperationException(
        "Connection string not provided. Use --connection-string option or set ConnectionStrings__DcbPostgres environment variable.");
}

// Implementation methods
static async Task BuildProjectionStatesAsync(string connectionString, int minEvents, string? projectorName, bool forceRebuild, bool verbose)
{
    Console.WriteLine("=== Multi Projection State Builder ===");
    Console.WriteLine($"Connection: {connectionString[..Math.Min(50, connectionString.Length)]}...");
    Console.WriteLine($"Min Events: {minEvents}");
    Console.WriteLine($"Force Rebuild: {forceRebuild}");
    Console.WriteLine();

    var services = BuildServices(connectionString);
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

static async Task ShowStatusAsync(string connectionString)
{
    Console.WriteLine("=== Projection State Status ===\n");

    var services = BuildServices(connectionString);
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

static ServiceProvider BuildServices(string connectionString)
{
    var services = new ServiceCollection();

    // Register domain types
    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);

    // Register Postgres DbContext factory
    services.AddPooledDbContextFactory<SekibanDcbDbContext>(options =>
    {
        options.UseNpgsql(connectionString);
    });

    // Register event store
    services.AddSingleton<IEventStore, PostgresEventStore>();

    // Register multi projection state store
    services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();

    // Register the builder
    services.AddSingleton<MultiProjectionStateBuilder>();

    return services.BuildServiceProvider();
}
