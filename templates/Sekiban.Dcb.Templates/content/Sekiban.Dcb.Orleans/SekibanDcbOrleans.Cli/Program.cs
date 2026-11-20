using Sekiban.Dcb.ServiceId;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
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

// This identity partitions all data; it must match the API and stay stable.
const string defaultServiceId = "sekiban-app";

// dcb_meta key of the CLI's cache completion marker (written only after a successful cache-sync)
const string CacheCompletionKey = "cli:cacheComplete";

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
        },
        "local": {
            "Database": "sqlite",
            "SqlitePath": "../SekibanDcbOrleans.ApiService/events.db"
        }
    },
    "DefaultProfile": "dev"
}

Usage:
  dotnet run -- status --profile dev
  dotnet run -- build -f --profile stg
  dotnet run -- profiles

Exit codes: 0 = success, 1 = failure (the error is written to stderr).
*/

// Create the root command
var rootCommand = new RootCommand("Sekiban DCB CLI - Manage projections, tags, and events");

// Profile option (shared across commands). -P is the profile; -p is always a projector name.
var profileOption = new Option<string?>("--profile", "-P")
{
    Description = "Profile name defined in user secrets (e.g., dev, stg, prod)"
};

// Database type option (shared across commands)
var databaseOption = new Option<string?>("--database", "-d")
{
    Description = "Database type: postgres, cosmos or sqlite (defaults to Sekiban:Database config or DATABASE_TYPE env var)"
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

// SQLite database file option
var sqlitePathOption = new Option<string?>("--sqlite-path")
{
    Description = "SQLite event store file used with --database sqlite (defaults to Sekiban:SqlitePath config or SQLITE_PATH env var)"
};

// Min events option
var minEventsOption = new Option<int>("--min-events", "-m")
{
    Description = "Minimum events required before building state",
    DefaultValueFactory = _ => int.TryParse(configuration["MIN_EVENTS"], out var val) ? val : 3000
};

// Projector name option (optional - if not specified, build all)
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
    Description = "Directory for local SQLite cache (one cache per connection is kept below it)",
    DefaultValueFactory = _ => configuration["CACHE_DIR"] ?? "./cache"
};

// Confirmation skip option for destructive commands
var yesOption = new Option<bool>("--yes", "-y")
{
    Description = "Skip the confirmation prompt (required when stdin is not interactive)"
};

Command WithConnectionOptions(Command command)
{
    command.Add(profileOption);
    command.Add(databaseOption);
    command.Add(connectionStringOption);
    command.Add(cosmosConnectionStringOption);
    command.Add(cosmosDatabaseNameOption);
    command.Add(sqlitePathOption);
    return command;
}

ConnectionTarget ResolveConnection(ParseResult parseResult)
{
    var profileConfig = ResolveProfile(configuration, parseResult.GetValue(profileOption));
    var databaseType = ResolveDatabaseType(profileConfig, parseResult.GetValue(databaseOption));
    var connectionString = ResolveConnectionString(
        profileConfig,
        databaseType,
        parseResult.GetValue(connectionStringOption),
        parseResult.GetValue(cosmosConnectionStringOption),
        parseResult.GetValue(sqlitePathOption));
    var cosmosDatabaseName = ResolveCosmosDatabaseName(profileConfig, parseResult.GetValue(cosmosDatabaseNameOption));
    var target = new ConnectionTarget(databaseType, connectionString, cosmosDatabaseName);
    // Fails early (exit 1) when the connection string cannot be parsed.
    _ = DescribeConnection(target);
    return target;
}

// Build command
var buildCommand = WithConnectionOptions(new Command("build", "Build multi projection states"));
buildCommand.Add(cacheModeOption);
buildCommand.Add(cacheDirOption);
buildCommand.Add(minEventsOption);
buildCommand.Add(projectorNameOption);
buildCommand.Add(forceRebuildOption);
buildCommand.Add(verboseOption);

buildCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    var cacheOptions = BuildCacheOptions(parseResult.GetValue(cacheModeOption), parseResult.GetValue(cacheDirOption), target);

    return await BuildProjectionStatesAsync(
        target,
        parseResult.GetValue(minEventsOption),
        parseResult.GetValue(projectorNameOption),
        parseResult.GetValue(forceRebuildOption),
        parseResult.GetValue(verboseOption),
        cacheOptions);
}));

// List command
var listCommand = new Command("list", "List all registered projectors");

listCommand.SetAction((parseResult, cancellationToken) => RunAsync(ListProjectorsAsync));

// Status command
var statusCommand = WithConnectionOptions(new Command("status", "Show status of all projection states"));
statusCommand.Add(cacheModeOption);
statusCommand.Add(cacheDirOption);

statusCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    var cacheOptions = BuildCacheOptions(parseResult.GetValue(cacheModeOption), parseResult.GetValue(cacheDirOption), target);
    return await ShowStatusAsync(target, cacheOptions);
}));

// Save command - export state JSON to file for debugging
var outputDirOption = new Option<string?>("--output-dir", "-o")
{
    Description = "Output directory for saved state files",
    DefaultValueFactory = _ => configuration["OUTPUT_DIR"] ?? "./output"
};

var saveCommand = WithConnectionOptions(new Command("save", "Save projection state JSON to files for debugging"));
saveCommand.Add(projectorNameOption);
saveCommand.Add(outputDirOption);

saveCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    return await SaveStateJsonAsync(
        target,
        parseResult.GetValue(projectorNameOption),
        parseResult.GetValue(outputDirOption) ?? "./output");
}));

// Delete command - delete projection states
var deleteCommand = WithConnectionOptions(new Command("delete", "Delete projection states"));
deleteCommand.Add(projectorNameOption);
deleteCommand.Add(yesOption);

deleteCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var assumeYes = parseResult.GetValue(yesOption);
    EnsureConfirmationPossible(assumeYes);
    var target = ResolveConnection(parseResult);
    return await DeleteStateAsync(target, parseResult.GetValue(projectorNameOption), assumeYes);
}));

// Tag-events command - fetch events for a specific tag
var tagOption = new Option<string>("--tag", "-t")
{
    Description = "Tag string in format 'group:content' (e.g., 'WeatherForecast:00000000-0000-0000-0000-000000000001')",
    Required = true
};

var tagEventsCommand = WithConnectionOptions(new Command("tag-events", "Fetch and save all events for a specific tag"));
tagEventsCommand.Add(cacheModeOption);
tagEventsCommand.Add(cacheDirOption);
tagEventsCommand.Add(tagOption);
tagEventsCommand.Add(outputDirOption);

tagEventsCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    var cacheOptions = BuildCacheOptions(parseResult.GetValue(cacheModeOption), parseResult.GetValue(cacheDirOption), target);
    return await FetchTagEventsAsync(
        target,
        parseResult.GetValue(tagOption) ?? "",
        parseResult.GetValue(outputDirOption) ?? "./output",
        cacheOptions);
}));

// Projection command - display current projection state
var projectionCommand = WithConnectionOptions(new Command("projection", "Display the current state of a projection"));
projectionCommand.Add(projectorNameOption);

projectionCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    return await ShowProjectionAsync(target, parseResult.GetValue(projectorNameOption));
}));

// Tag-state command - project events for a tag using a specific tag projector
var tagProjectorOption = new Option<string?>("--projector", "-p")
{
    Description = "Tag projector name to use for projection. Auto-detects if not specified (e.g., 'UserMonthlyReservation' tag uses 'UserMonthlyReservationProjector')"
};

var tagStateCommand = WithConnectionOptions(new Command("tag-state", "Project and display the current state for a specific tag"));
tagStateCommand.Add(cacheModeOption);
tagStateCommand.Add(cacheDirOption);
tagStateCommand.Add(tagOption);
tagStateCommand.Add(tagProjectorOption);

tagStateCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    var cacheOptions = BuildCacheOptions(parseResult.GetValue(cacheModeOption), parseResult.GetValue(cacheDirOption), target);
    return await ShowTagStateAsync(
        target,
        parseResult.GetValue(tagOption) ?? "",
        parseResult.GetValue(tagProjectorOption) ?? "",
        cacheOptions);
}));

// Tag-list command - list all tags in the event store
var tagGroupOption = new Option<string?>("--tag-group", "-g")
{
    Description = "Filter by tag group name (e.g., 'WeatherForecast')"
};

var tagListCommand = WithConnectionOptions(new Command("tag-list", "List all tags in the event store and export to JSON"));
tagListCommand.Add(cacheModeOption);
tagListCommand.Add(cacheDirOption);
tagListCommand.Add(tagGroupOption);
tagListCommand.Add(outputDirOption);

tagListCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    var cacheOptions = BuildCacheOptions(parseResult.GetValue(cacheModeOption), parseResult.GetValue(cacheDirOption), target);
    return await ListTagsAsync(
        target,
        parseResult.GetValue(tagGroupOption),
        parseResult.GetValue(outputDirOption) ?? "./output",
        cacheOptions);
}));

// Safe window option (minutes)
var safeWindowOption = new Option<int>("--safe-window")
{
    Description = "Safe window in minutes (events within this window are not cached)",
    DefaultValueFactory = _ => int.TryParse(configuration["SAFE_WINDOW_MINUTES"], out var val) ? val : 10
};

// Cache-sync command - sync remote events to local SQLite cache
var cacheSyncCommand = WithConnectionOptions(new Command("cache-sync", "Sync remote events to local SQLite cache"));
cacheSyncCommand.Add(cacheDirOption);
cacheSyncCommand.Add(safeWindowOption);
cacheSyncCommand.Add(verboseOption);

cacheSyncCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var target = ResolveConnection(parseResult);
    return await SyncCacheAsync(
        target,
        parseResult.GetValue(cacheDirOption) ?? "./cache",
        parseResult.GetValue(safeWindowOption),
        parseResult.GetValue(verboseOption),
        cancellationToken);
}));

// Cache-stats command - show cache statistics
var cacheStatsCommand = new Command("cache-stats", "Show local cache statistics (all connections cached under the cache directory)")
{
    cacheDirOption
};

cacheStatsCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
    await ShowCacheStatsAsync(parseResult.GetValue(cacheDirOption) ?? "./cache")));

// Cache-clear command - clear local cache
var cacheClearCommand = new Command("cache-clear", "Clear local SQLite cache (all connections cached under the cache directory)")
{
    cacheDirOption,
    yesOption
};

cacheClearCommand.SetAction((parseResult, cancellationToken) => RunAsync(async () =>
{
    var assumeYes = parseResult.GetValue(yesOption);
    EnsureConfirmationPossible(assumeYes);
    return await ClearCacheAsync(parseResult.GetValue(cacheDirOption) ?? "./cache", assumeYes);
}));

// Profiles command - list available profiles
var profilesCommand = new Command("profiles", "List available profiles defined in user secrets");
profilesCommand.SetAction((parseResult, cancellationToken) => RunAsync(() =>
{
    ListProfiles(configuration);
    return Task.FromResult(0);
}));

rootCommand.Add(buildCommand);
rootCommand.Add(listCommand);
rootCommand.Add(statusCommand);
rootCommand.Add(saveCommand);
rootCommand.Add(deleteCommand);
rootCommand.Add(tagEventsCommand);
rootCommand.Add(projectionCommand);
rootCommand.Add(tagStateCommand);
rootCommand.Add(tagListCommand);
rootCommand.Add(cacheSyncCommand);
rootCommand.Add(cacheStatsCommand);
rootCommand.Add(cacheClearCommand);
rootCommand.Add(profilesCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();

// Runs a command body: failures are written to stderr and turned into exit code 1.
static async Task<int> RunAsync(Func<Task<int>> action)
{
    try
    {
        return await action();
    }
    catch (CliException ex)
    {
        WriteError(ex.Message);
        return 1;
    }
    catch (Exception ex)
    {
        var message = ex.InnerException is null || ex.InnerException.Message == ex.Message
            ? ex.Message
            : $"{ex.Message} ({ex.InnerException.Message})";
        WriteError($"{ex.GetType().Name}: {message}");
        return 1;
    }
}

static void WriteError(string message)
{
    Console.Out.Flush();
    Console.Error.WriteLine($"Error: {message}");
}

// Fails fast (before touching any store) when a prompt would be needed but cannot be answered.
static void EnsureConfirmationPossible(bool assumeYes)
{
    if (!assumeYes && Console.IsInputRedirected)
    {
        throw new CliException("Confirmation required, but stdin is not interactive. Re-run with --yes (-y) to proceed without a prompt.");
    }
}

static bool Confirm(string prompt, bool assumeYes)
{
    if (assumeYes)
    {
        return true;
    }

    EnsureConfirmationPossible(assumeYes);
    Console.Write(prompt);
    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
    return response is "y" or "yes";
}

static string GetServiceId()
{
    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .Build();
    return ServiceIdValidator.NormalizeAndValidate(configuration["Sekiban:ServiceId"] ?? defaultServiceId);
}

static IServiceIdProvider CreateServiceIdProvider() => new FixedServiceIdProvider(GetServiceId());

// Root directory for every cache of this service: <cache-dir>/<service id>
static string GetCacheRoot(string cacheDir) => Path.Combine(cacheDir, Uri.EscapeDataString(GetServiceId()));

// Helper to resolve profile configuration
static IConfiguration ResolveProfile(IConfiguration configuration, string? profileName)
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
            throw new CliException(
                "No profile specified. Use --profile option.\n" +
                $"Available profiles: {string.Join(", ", profileEntries.Select(p => p.Key))}");
        }
        else
        {
            // No profiles defined, use root configuration
            return configuration;
        }
    }

    if (!hasProfiles)
    {
        throw new CliException($"Profile '{resolvedProfile}' is not defined. Add it to Profiles in User Secrets.");
    }

    var profileSection = profilesSection.GetSection(resolvedProfile!);
    if (!profileSection.Exists())
    {
        throw new CliException(
            $"Profile '{resolvedProfile}' not found.\n" +
            $"Available profiles: {string.Join(", ", profileEntries.Select(p => p.Key))}");
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
    MapAlias("SqlitePath", "Sekiban:SqlitePath");

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
    var value = cliOption;
    if (string.IsNullOrEmpty(value))
        value = config["Sekiban:Database"];
    if (string.IsNullOrEmpty(value))
        value = config["DATABASE_TYPE"];
    if (string.IsNullOrEmpty(value))
        return "postgres";

    return value.Trim().ToLowerInvariant() switch
    {
        "postgres" or "postgresql" => "postgres",
        "cosmos" or "cosmosdb" => "cosmos",
        "sqlite" => "sqlite",
        _ => throw new CliException($"Unknown database type '{value}'. Use postgres, cosmos or sqlite.")
    };
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

// Helper to resolve connection string (or SQLite file path) from argument, config, or environment
static string ResolveConnectionString(IConfiguration config, string databaseType, string? connectionString, string? cosmosConnectionString, string? sqlitePath)
{
    if (databaseType == "cosmos")
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

        throw new CliException(
            "Cosmos DB connection string not provided.\n" +
            "Options:\n" +
            "  1. Use --cosmos-connection-string option\n" +
            "  2. Set user secret: dotnet user-secrets set \"ConnectionStrings:SekibanDcbCosmos\" \"your-connection-string\"\n" +
            "  3. Set environment variable: COSMOS_CONNECTION_STRING");
    }

    if (databaseType == "sqlite")
    {
        // Priority: CLI option > Sekiban:SqlitePath > SQLITE_PATH env
        var path = sqlitePath;
        if (string.IsNullOrEmpty(path))
            path = config["Sekiban:SqlitePath"];
        if (string.IsNullOrEmpty(path))
            path = config["SQLITE_PATH"];
        if (string.IsNullOrEmpty(path))
        {
            throw new CliException(
                "SQLite database path not provided.\n" +
                "Options:\n" +
                "  1. Use --sqlite-path option (the ApiService writes events.db in its working directory)\n" +
                "  2. Set user secret: dotnet user-secrets set \"Sekiban:SqlitePath\" \"path/to/events.db\"\n" +
                "  3. Set environment variable: SQLITE_PATH");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new CliException($"SQLite database not found: {path}");
        }

        return fullPath;
    }

    // Priority: CLI option > ConnectionStrings:DcbPostgres > CONNECTION_STRING env
    if (!string.IsNullOrEmpty(connectionString))
        return connectionString;

    var postgresConnStr = config["ConnectionStrings:DcbPostgres"];
    if (!string.IsNullOrEmpty(postgresConnStr))
        return postgresConnStr;

    var postgresEnvConnStr = config["CONNECTION_STRING"];
    if (!string.IsNullOrEmpty(postgresEnvConnStr))
        return postgresEnvConnStr;

    throw new CliException(
        "PostgreSQL connection string not provided.\n" +
        "Options:\n" +
        "  1. Use --connection-string option\n" +
        "  2. Set user secret: dotnet user-secrets set \"ConnectionStrings:DcbPostgres\" \"your-connection-string\"\n" +
        "  3. Set environment variable: CONNECTION_STRING");
}

// Connection without secrets (allowlisted keys only); safe to print or store in cache metadata.
static string DescribeConnection(ConnectionTarget target) =>
    ConnectionIdentity.Describe(target.DatabaseType, target.ConnectionString, target.CosmosDatabaseName);

// Random 32-byte HMAC key for cache folder names, stored once in the cache root (readable by the user only where the OS
// allows). Returns null when it does not exist and create is false. Throws CliException when it cannot be created/read.
static byte[]? ReadOrCreateCacheKey(string cacheRoot, bool create)
{
    var keyPath = Path.Combine(cacheRoot, ".cache-key");
    try
    {
        if (!File.Exists(keyPath))
        {
            if (!create)
            {
                return null;
            }

            // Concurrent first runs: only one creates the key (under an exclusive lock, re-checking existence); the key
            // is written to a temporary file and renamed into place, so readers never see a partial key.
            Directory.CreateDirectory(cacheRoot);
            using var keyLock = AcquireLockWithRetry(Path.Combine(cacheRoot, ".cache-key.lock"), TimeSpan.FromSeconds(10));
            if (!File.Exists(keyPath))
            {
                var tempKeyPath = Path.Combine(cacheRoot, $".cache-key.{Guid.NewGuid():N}.tmp");
                var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows())
                {
                    fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }
                using (var stream = new FileStream(tempKeyPath, fileOptions))
                {
                    stream.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                }
                File.Move(tempKeyPath, keyPath);
            }
        }

        // A key file is always written completely before it appears; retry briefly anyway.
        for (var attempt = 0; ; attempt++)
        {
            var key = File.ReadAllBytes(keyPath);
            if (key.Length == 32)
            {
                return key;
            }
            if (attempt >= 4)
            {
                throw new CliException($"Cache key file {keyPath} is invalid (expected 32 bytes). Delete it together with the cache folders.");
            }
            Thread.Sleep(50);
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        throw new CliException($"Cache key file {keyPath} could not be {(create ? "created or read" : "read")}: {ex.Message}");
    }
}

// Exclusive lock file, waiting up to the timeout for another process to release it.
static FileStream AcquireLockWithRetry(string lockPath, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (true)
    {
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) when (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }
    }
}

// Exclusive per-folder lock held for a whole cache-sync (and by cache-clear while it deletes). Null when another
// process holds it.
static FileStream? TryAcquireCacheLock(string cacheFolder)
{
    try
    {
        return new FileStream(Path.Combine(cacheFolder, ".sync.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException)
    {
        return null;
    }
}

// Connection to an existing SQLite file that is never created by opening it.
static SqliteConnection OpenExistingSqlite(string path)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
    connection.Open();
    return connection;
}

// <cache root>/<database type>-<16 hex>/events.db (see ConnectionIdentity.ComputeKey). Null when no key exists yet
// (then no cache exists either) and create is false.
static string? GetCachePath(string cacheDir, ConnectionTarget target, bool createKey)
{
    var cacheRoot = GetCacheRoot(cacheDir);
    var key = ReadOrCreateCacheKey(cacheRoot, createKey);
    return key == null
        ? null
        : Path.Combine(cacheRoot, ConnectionIdentity.ComputeKey(target.DatabaseType, target.ConnectionString, target.CosmosDatabaseName, key), "events.db");
}

static CacheOptions BuildCacheOptions(string? cacheMode, string? cacheDir, ConnectionTarget target)
{
    var resolvedMode = (cacheMode ?? "auto").Trim().ToLowerInvariant() switch
    {
        "auto" or "" => SimpleCacheMode.Auto,
        "off" => SimpleCacheMode.Off,
        "clear" => SimpleCacheMode.Clear,
        "cache-only" or "cacheonly" => SimpleCacheMode.CacheOnly,
        _ => throw new CliException($"Unknown cache mode '{cacheMode}'. Use auto, off, clear or cache-only.")
    };

    var resolvedCacheDir = string.IsNullOrWhiteSpace(cacheDir) ? "./cache" : cacheDir;
    if (resolvedMode == SimpleCacheMode.Off)
    {
        return new CacheOptions(resolvedMode, resolvedCacheDir, "");
    }

    try
    {
        // Read commands never create the key: without a key no cache exists for any connection.
        return new CacheOptions(resolvedMode, resolvedCacheDir, GetCachePath(resolvedCacheDir, target, createKey: false) ?? "");
    }
    catch (CliException ex) when (resolvedMode != SimpleCacheMode.CacheOnly)
    {
        Console.Out.Flush();
        Console.Error.WriteLine($"Warning: the local cache is not used for this command: {ex.Message}");
        return new CacheOptions(SimpleCacheMode.Off, resolvedCacheDir, "");
    }
}

static bool TryResolveCachePath(CacheOptions cacheOptions, out string? cachePath)
{
    var resolvedPath = cacheOptions.CachePath;

    if (cacheOptions.Mode == SimpleCacheMode.Clear && resolvedPath.Length > 0 && File.Exists(resolvedPath))
    {
        Console.WriteLine("Clearing existing cache...");
        DeleteCacheFiles(resolvedPath);
    }

    var useCache = false;
    string? notUsableReason = null;
    if (cacheOptions.Mode is SimpleCacheMode.Auto or SimpleCacheMode.CacheOnly && resolvedPath.Length > 0 && File.Exists(resolvedPath))
    {
        // Only a cache that a successful cache-sync completed (it carries the CLI completion marker) is used.
        var (isComplete, error) = GetCacheCompletionStatus(resolvedPath);
        useCache = isComplete;
        notUsableReason = error != null
            ? $"the cache could not be read ({error})"
            : isComplete ? null : "the cache has no completion marker (incomplete or old sync)";
    }

    if (notUsableReason != null)
    {
        if (cacheOptions.Mode == SimpleCacheMode.CacheOnly)
        {
            throw new CliException(
                $"SQLite cache for this connection is not usable: {resolvedPath}: {notUsableReason}.\n" +
                "Run 'cache-sync' with the same connection settings again.");
        }
        Console.Out.Flush();
        Console.Error.WriteLine($"Warning: {resolvedPath}: {notUsableReason}; reading the database without cache.");
    }

    if (cacheOptions.Mode == SimpleCacheMode.CacheOnly && !useCache)
    {
        throw new CliException(
            $"SQLite cache for this connection not found{(resolvedPath.Length > 0 ? ": " + resolvedPath : "")}\n" +
            "Run 'cache-sync' with the same connection settings first to create the cache.");
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
        Console.WriteLine("No cache for this connection, using remote database...");
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

// Opens a SQLite file read-only (never creates or changes it) and checks whether a table exists.
static bool SqliteTableExists(string path, string tableName)
{
    // Read-only, no pooling: never creates or changes the file. Throws SqliteException when the file cannot be read.
    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString();
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
    command.Parameters.AddWithValue("$name", tableName);
    return Convert.ToInt64(command.ExecuteScalar()) > 0;
}

// Throws unless the file exists and is a Sekiban DCB SQLite event store (has the dcb_events table).
static void EnsureSekibanSqliteStore(string path)
{
    if (!File.Exists(path))
    {
        throw new CliException($"SQLite database not found: {path}");
    }
    bool hasEvents;
    try
    {
        hasEvents = SqliteTableExists(path, "dcb_events");
    }
    catch (SqliteException ex)
    {
        throw new CliException($"'{path}' could not be read as a SQLite database ({ex.Message}). The file was not modified.");
    }
    if (!hasEvents)
    {
        throw new CliException($"'{path}' is not a Sekiban DCB SQLite event store (no dcb_events table). The file was not modified.");
    }
}

// SQLite "database path" for the Sekiban stores that opens the file read-only. The stores build
// "Data Source={path}", so the read-only mode is appended as another connection string keyword.
static string ToReadOnlySqlitePath(string path) => ValidateSqliteStorePath(path) + ";Mode=ReadOnly";

static string ValidateSqliteStorePath(string path) =>
    TryToReadOnlySqlitePath(path) is null
        ? throw new CliException(
        $"Cannot open SQLite file '{path}': the path contains ';', '=' or a quote, which cannot be passed to " +
        "the Sekiban SQLite stores. Move or rename the file (or use a symbolic link) to a path without these characters.")
        : path;

static string? TryToReadOnlySqlitePath(string path) =>
    path.IndexOfAny([';', '"', '\'', '=']) >= 0 ? null : $"{path};Mode=ReadOnly";

static void PrintTarget(ConnectionTarget target)
{
    Console.WriteLine($"Database: {target.DatabaseType}");
    Console.WriteLine($"Connection: {DescribeConnection(target)}");
}

// Throws when a projector name is given but is not registered in the domain.
static void EnsureProjectorExists(DcbDomainTypes domainTypes, string? projectorName)
{
    if (string.IsNullOrEmpty(projectorName))
    {
        return;
    }

    var names = domainTypes.MultiProjectorTypes.GetAllProjectorNames();
    if (!names.Contains(projectorName))
    {
        throw new CliException(
            $"Unknown projector '{projectorName}'.\n" +
            $"Registered projectors: {string.Join(", ", names)}");
    }
}

// For storage-management commands (save, delete, projection): a projector name is accepted when it is
// registered in the domain OR has stored states (e.g. a renamed or removed projector). Returns the matching states.
static IReadOnlyList<ProjectorStateInfo> FilterStoredStates(
    IReadOnlyList<ProjectorStateInfo> states,
    string? projectorName)
{
    if (string.IsNullOrEmpty(projectorName))
    {
        return states;
    }

    var matching = states.Where(s => s.ProjectorName == projectorName).ToList();
    var registered = DomainType.GetDomainTypes().MultiProjectorTypes.GetAllProjectorNames();
    if (matching.Count == 0 && !registered.Contains(projectorName))
    {
        throw new CliException(
            $"Unknown projector '{projectorName}': it is neither registered nor stored.\n" +
            $"Registered projectors: {string.Join(", ", registered)}\n" +
            $"Stored projectors: {string.Join(", ", states.Select(s => s.ProjectorName).Distinct())}");
    }
    return matching;
}

// Lists stored projection states. A SQLite event store without the state table simply has no states
// (the read-only commands do not create the table).
static async Task<IReadOnlyList<ProjectorStateInfo>> ListStoredStatesAsync(ServiceProvider services, ConnectionTarget target)
{
    if (target.DatabaseType == "sqlite" && !SqliteTableExists(target.ConnectionString, "dcb_multi_projection_states"))
    {
        return [];
    }
    return await services.GetRequiredService<IMultiProjectionStateStore>().ListAllAsync();
}

// File-name safe form of a name or version (e.g. projector version "1.0.0" stays as is).
static string ToSafeFileNamePart(string value)
{
    var invalid = Path.GetInvalidFileNameChars().Concat([':', '/', '\\', ' ']).ToHashSet();
    return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
}

// Implementation methods
static async Task<int> BuildProjectionStatesAsync(
    ConnectionTarget target,
    int minEvents,
    string? projectorName,
    bool forceRebuild,
    bool verbose,
    CacheOptions cacheOptions)
{
    EnsureProjectorExists(DomainType.GetDomainTypes(), projectorName);

    Console.WriteLine("=== Multi Projection State Builder ===");
    PrintTarget(target);
    Console.WriteLine($"Min Events: {minEvents}");
    Console.WriteLine($"Force Rebuild: {forceRebuild}");

    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return 1;
    }
    Console.WriteLine();

    var services = BuildServices(target, cachePath: cachePath, readOnly: false);

    // The builder turns event-count and state-read errors into "Skipped" results. Read both once up front so an
    // unreachable or broken store is reported as an error (exit 1) instead of a skip.
    await services.GetRequiredService<IEventStore>().GetEventCountAsync();
    await ListStoredStatesAsync(services, target);

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
        if (IsBuildError(result))
        {
            throw new CliException($"Build failed for {result.ProjectorName}: {result.Reason}");
        }
        return 0;
    }

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
    var skipped = results.Count(r => r.Status == BuildStatus.Skipped && !IsBuildError(r));
    var failed = results.Count(IsBuildError);

    Console.WriteLine(new string('=', 80));
    Console.WriteLine($"Summary: {succeeded} succeeded, {skipped} skipped, {failed} failed");

    if (failed > 0)
    {
        var reasons = results
            .Where(IsBuildError)
            .Select(r => $"  {r.ProjectorName}: {r.Reason}");
        throw new CliException($"{failed} projector build(s) failed:\n{string.Join("\n", reasons)}");
    }
    return 0;
}

// Failed results, and "Skipped" results that the builder produced from an error
// ("Failed to get event count: ..." / "Failed to get current state: ...").
static bool IsBuildError(ProjectorBuildResult result) =>
    result.Status == BuildStatus.Failed ||
    (result.Status == BuildStatus.Skipped && result.Reason.StartsWith("Failed to ", StringComparison.Ordinal));

static Task<int> ListProjectorsAsync()
{
    Console.WriteLine("=== Registered Projectors ===\n");

    var domainTypes = DomainType.GetDomainTypes();
    var projectorNames = domainTypes.MultiProjectorTypes.GetAllProjectorNames();

    Console.WriteLine($"Total: {projectorNames.Count} projector(s)\n");

    foreach (var name in projectorNames)
    {
        var version = domainTypes.MultiProjectorTypes.GetProjectorVersion(name);
        Console.WriteLine($"  - {name} (version: {version})");
    }

    return Task.FromResult(0);
}

static async Task<int> ShowStatusAsync(ConnectionTarget target, CacheOptions cacheOptions)
{
    Console.WriteLine("=== Projection State Status ===\n");
    PrintTarget(target);
    Console.WriteLine();

    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return 1;
    }
    Console.WriteLine();

    var services = BuildServices(target, cachePath: cachePath);
    var eventStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Get total event count (a connection failure is an error, not "0 events")
    var totalEvents = await eventStore.GetEventCountAsync();
    Console.WriteLine($"Total Events in Store: {totalEvents:N0}\n");

    // List all states
    var states = await ListStoredStatesAsync(services, target);
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
            var version = domainTypes.MultiProjectorTypes.GetProjectorVersion(name);
            Console.WriteLine($"  - {name} (version: {version})");
        }
    }
    return 0;
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

// Reads the state bytes. Error is set when reading failed (an empty state is Data = [] with no error).
static async Task<(byte[]? Data, string? Error)> ReadStateDataAsync(IMultiProjectionStateStore stateStore, MultiProjectionStateRecord state)
{
    try
    {
        await using var stateStream = await stateStore.OpenStateDataReadStreamAsync(state).ConfigureAwait(false);
        using var ms = new MemoryStream();
        await stateStream.CopyToAsync(ms).ConfigureAwait(false);
        return (ms.ToArray(), null);
    }
    catch (Exception ex)
    {
        return (null, ex.Message);
    }
}

static async Task<int> SaveStateJsonAsync(ConnectionTarget target, string? projectorName, string outputDir)
{
    Console.WriteLine("=== Save Projection State JSON ===\n");
    PrintTarget(target);
    Console.WriteLine($"Output Directory: {outputDir}");
    Console.WriteLine();

    var services = BuildServices(target);

    // Get list of states (summary info)
    var stateInfos = FilterStoredStates(await ListStoredStatesAsync(services, target), projectorName);
    if (stateInfos.Count == 0)
    {
        Console.WriteLine("No projection states found.");
        return 0;
    }
    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();

    // Create output directory
    Directory.CreateDirectory(outputDir);

    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var savedCount = 0;
    var failedCount = 0;

    foreach (var info in stateInfos)
    {
        Console.WriteLine($"Saving: {info.ProjectorName} (v{info.ProjectorVersion})");

        // Fetch the full record with state data
        MultiProjectionStateRecord? state;
        try
        {
            state = await stateStore.GetLatestForVersionAsync(info.ProjectorName, info.ProjectorVersion);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error: Could not fetch full record for {info.ProjectorName} (v{info.ProjectorVersion}): {ex.Message}");
            failedCount++;
            continue;
        }

        if (state is null)
        {
            Console.Error.WriteLine($"  Error: Could not fetch full record for {info.ProjectorName} (v{info.ProjectorVersion})");
            failedCount++;
            continue;
        }

        // One file set per projector version: <projector>_v<version>_<timestamp>_*.json
        var fileBaseName = $"{ToSafeFileNamePart(state.ProjectorName)}_v{ToSafeFileNamePart(state.ProjectorVersion)}_{timestamp}";

        // Save metadata
        var metadataFileName = $"{fileBaseName}_metadata.json";
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
        var (stateData, readError) = await ReadStateDataAsync(stateStore, state);
        if (readError != null)
        {
            if (state.IsOffloaded)
            {
                // This CLI has no blob storage access: offloaded state bytes are not downloaded.
                Console.WriteLine($"  State is offloaded to: {state.OffloadKey} (not downloaded; only metadata saved)");
                Console.WriteLine();
                savedCount++;
                continue;
            }
            Console.Error.WriteLine($"  Error: Could not read state data of {state.ProjectorName} (v{state.ProjectorVersion}): {readError}");
            failedCount++;
            continue;
        }
        if (stateData != null)
        {
            var stateFileName = $"{fileBaseName}_state.json";
            var statePath = Path.Combine(outputDir, stateFileName);

            try
            {
                // v10 format: state bytes are plain JSON (UTF-8), not Gzip compressed at outer level
                // The inner payload may be compressed via custom serializer
                string jsonContent;

                // Auto-detect: Gzip (v9) or plain JSON (v10)
                if (stateData.Length >= 2 && stateData[0] == 0x1f && stateData[1] == 0x8b)
                {
                    // v9 format: Gzip compressed
                    jsonContent = GzipCompression.DecompressToString(stateData);
                }
                else
                {
                    // v10 format: Plain UTF-8 JSON
                    jsonContent = System.Text.Encoding.UTF8.GetString(stateData);
                }

                // Pretty print the JSON
                var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
                var prettyJson = System.Text.Json.JsonSerializer.Serialize(jsonDoc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(statePath, prettyJson);
                Console.WriteLine($"  State: {stateFileName} ({FormatBytes(stateData.LongLength)})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Could not decode state as JSON: {ex.Message}");
                // Save raw bytes as fallback
                var rawFileName = $"{fileBaseName}_state.bin";
                var rawPath = Path.Combine(outputDir, rawFileName);
                await File.WriteAllBytesAsync(rawPath, stateData);
                Console.WriteLine($"  Raw state saved: {rawFileName}");
            }
        }

        Console.WriteLine();
        savedCount++;
    }

    Console.WriteLine($"Saved {savedCount} projection state(s) to {outputDir}");
    if (failedCount > 0)
    {
        throw new CliException($"{failedCount} projection state(s) could not be saved.");
    }
    return 0;
}

static async Task<int> DeleteStateAsync(ConnectionTarget target, string? projectorName, bool assumeYes)
{
    Console.WriteLine("=== Delete Projection States ===\n");
    PrintTarget(target);
    Console.WriteLine();

    // List through the read-only path first; the store is opened for writing only after confirmation.
    var readServices = BuildServices(target);

    // Get states to delete
    var states = FilterStoredStates(await ListStoredStatesAsync(readServices, target), projectorName);

    if (states.Count == 0)
    {
        Console.WriteLine("No projection states found to delete.");
        return 0;
    }

    Console.WriteLine($"Found {states.Count} state(s) to delete:");
    foreach (var state in states)
    {
        Console.WriteLine($"  - {state.ProjectorName} (v{state.ProjectorVersion})");
    }
    Console.WriteLine();

    if (!Confirm("Are you sure you want to delete these states? (y/N): ", assumeYes))
    {
        Console.WriteLine("Cancelled.");
        return 0;
    }

    // delete writes to the store (the SQLite state store may also upgrade its schema)
    var stateStore = BuildServices(target, readOnly: false).GetRequiredService<IMultiProjectionStateStore>();

    var deleted = 0;
    foreach (var state in states)
    {
        try
        {
            await stateStore.DeleteAsync(state.ProjectorName, state.ProjectorVersion);
            Console.WriteLine($"  Deleted: {state.ProjectorName} (v{state.ProjectorVersion})");
            deleted++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to delete {state.ProjectorName} (v{state.ProjectorVersion}): {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Deleted {deleted} of {states.Count} state(s)");
    if (deleted < states.Count)
    {
        throw new CliException($"{states.Count - deleted} state(s) could not be deleted.");
    }
    return 0;
}

static async Task<int> FetchTagEventsAsync(
    ConnectionTarget target,
    string tagString,
    string outputDir,
    CacheOptions cacheOptions)
{
    Console.WriteLine("=== Fetch Tag Events ===\n");
    PrintTarget(target);
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine($"Output Directory: {outputDir}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return 1;
    }
    Console.WriteLine();

    var services = BuildServices(target, cachePath: cachePath);
    var eventStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // Parse the tag string
    var tag = domainTypes.TagTypes.GetTag(tagString);
    Console.WriteLine($"Parsed Tag: {tag.GetType().Name} = {tag}");
    Console.WriteLine();

    // Fetch events for the tag
    Console.WriteLine("Fetching events...");
    var events = (await eventStore.ReadEventsByTagAsync(tag, domainTypes.EventTypes)).ToList();
    Console.WriteLine($"Found {events.Count} event(s)\n");

    if (events.Count == 0)
    {
        Console.WriteLine("No events to save.");
        return 0;
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
    var safeTagName = ToSafeFileNamePart(tagString);
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
    return 0;
}

static async Task<int> ShowProjectionAsync(ConnectionTarget target, string? projectorName)
{
    var domainTypes = DomainType.GetDomainTypes();

    Console.WriteLine("=== Projection State ===\n");
    PrintTarget(target);
    Console.WriteLine();

    var services = BuildServices(target);

    // Get list of states
    var stateInfos = FilterStoredStates(await ListStoredStatesAsync(services, target), projectorName);

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
        return 0;
    }

    var stateStore = services.GetRequiredService<IMultiProjectionStateStore>();
    var readFailures = 0;
    foreach (var info in stateInfos)
    {
        Console.WriteLine($"Projector: {info.ProjectorName}");
        Console.WriteLine($"  Version: {info.ProjectorVersion}");
        Console.WriteLine($"  Events Processed: {info.EventsProcessed:N0}");
        Console.WriteLine($"  Size: {FormatBytes(info.CompressedSizeBytes)}");
        Console.WriteLine($"  Updated: {info.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        // Fetch the full record to show state details
        var state = await stateStore.GetLatestForVersionAsync(info.ProjectorName, info.ProjectorVersion);
        if (state is null)
        {
            // Listed a moment ago but gone now (deleted or replaced concurrently)
            Console.Error.WriteLine($"  Error: Could not fetch the state record of {info.ProjectorName} (v{info.ProjectorVersion}).");
            readFailures++;
            continue;
        }

        Console.WriteLine($"  Last SortableUniqueId: {state.LastSortableUniqueId}");
        Console.WriteLine($"  Safe Window Threshold: {state.SafeWindowThreshold}");
        Console.WriteLine($"  Build Source: {state.BuildSource}");
        Console.WriteLine($"  Build Host: {state.BuildHost}");
        Console.WriteLine($"  Is Offloaded: {state.IsOffloaded}");

        var (stateData, readError) = await ReadStateDataAsync(stateStore, state);
        if (readError != null && !state.IsOffloaded)
        {
            Console.Error.WriteLine($"  Error: Could not read state data: {readError}");
            readFailures++;
        }
        else if (stateData != null)
        {
            try
            {
                // Try to deserialize and show a preview of the state
                string jsonContent;

                // Auto-detect format
                if (stateData.Length >= 2 && stateData[0] == 0x1f && stateData[1] == 0x8b)
                {
                    jsonContent = GzipCompression.DecompressToString(stateData);
                }
                else
                {
                    jsonContent = System.Text.Encoding.UTF8.GetString(stateData);
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
                Console.Error.WriteLine($"  Error: Could not decode state data of {info.ProjectorName} (v{info.ProjectorVersion}): {ex.Message}");
                readFailures++;
            }
        }

        Console.WriteLine();
    }
    if (readFailures > 0)
    {
        throw new CliException($"State data of {readFailures} projector(s) could not be read.");
    }
    return 0;
}

static async Task<int> ShowTagStateAsync(
    ConnectionTarget target,
    string tagString,
    string? projectorName,
    CacheOptions cacheOptions)
{
    var domainTypes = DomainType.GetDomainTypes();
    var tagProjectorNames = domainTypes.TagProjectorTypes.GetAllProjectorNames();

    // Resolve the tag projector before connecting: unknown names are argument errors.
    var resolvedProjectorName = projectorName;
    var autoDetected = false;
    if (string.IsNullOrEmpty(resolvedProjectorName))
    {
        // Extract tag group from tag string (e.g., "UserMonthlyReservation:xxx" -> "UserMonthlyReservation")
        var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
        resolvedProjectorName = domainTypes.TagProjectorTypes.TryGetProjectorForTagGroup(tagGroup);
        if (resolvedProjectorName == null)
        {
            throw new CliException(
                $"Could not auto-detect a tag projector from tag group '{tagGroup}'. Use --projector (-p).\n" +
                $"Available tag projectors: {string.Join(", ", tagProjectorNames)}\n" +
                $"Available tag groups: {string.Join(", ", domainTypes.TagTypes.GetAllTagGroupNames())}");
        }
        autoDetected = true;
    }
    else if (!tagProjectorNames.Contains(resolvedProjectorName))
    {
        throw new CliException(
            $"Unknown tag projector '{resolvedProjectorName}'.\n" +
            $"Available tag projectors: {string.Join(", ", tagProjectorNames)}");
    }

    Console.WriteLine("=== Tag State Projection ===\n");
    PrintTarget(target);
    Console.WriteLine($"Tag: {tagString}");
    Console.WriteLine(autoDetected
        ? $"Projector: {resolvedProjectorName} (auto-detected from the tag group)"
        : $"Projector: {resolvedProjectorName}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return 1;
    }
    Console.WriteLine();

    var services = BuildServices(target, cachePath: cachePath);
    var tagStateService = services.GetRequiredService<TagStateService>();

    // Project the tag state
    Console.WriteLine("Projecting events...\n");
    var projectionResult = await tagStateService.ProjectTagStateAsync(tagString, resolvedProjectorName);

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
    return 0;
}

// readOnly (default): SQLite stores are opened read-only and never created, migrated or switched to WAL.
// Only build and delete pass readOnly: false.
static ServiceProvider BuildServices(
    ConnectionTarget target,
    bool enableProgressReporting = true,
    string? cachePath = null,
    bool readOnly = true)
{
    var services = new ServiceCollection();
    var connectionString = target.ConnectionString;

    // Register domain types
    var domainTypes = DomainType.GetDomainTypes();
    services.AddSingleton(domainTypes);
    services.AddSingleton(CreateServiceIdProvider());

    var useCache = !string.IsNullOrWhiteSpace(cachePath);
    if (useCache)
    {
        // The cache was created by cache-sync; read it without schema initialization.
        services.AddSingleton<IEventStore>(sp =>
            new SqliteEventStore(
                cachePath!,
                sp.GetRequiredService<DcbDomainTypes>().EventTypes,
                new SqliteEventStoreOptions { AutoCreateDatabase = false },
                serviceIdProvider: sp.GetRequiredService<IServiceIdProvider>()));
    }

    if (target.DatabaseType == "cosmos")
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
        var cosmosContext = new CosmosDbContext(connectionString, target.CosmosDatabaseName, options: cosmosOptions);
        services.AddSingleton(cosmosOptions);

        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        services.AddSingleton(cosmosContext);
        if (!useCache)
        {
            services.AddSingleton<IEventStore, CosmosDbEventStore>();
        }
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();
    }
    else if (target.DatabaseType == "sqlite")
    {
        // SQLite event store written by the ApiService (Sekiban:Database=sqlite); connectionString is the file path.
        // It must already be a Sekiban store: the CLI never creates one.
        EnsureSekibanSqliteStore(connectionString);
        // The Sekiban SQLite stores interpolate the path into a connection string: the same path rule for all commands.
        var storePath = readOnly ? ToReadOnlySqlitePath(connectionString) : ValidateSqliteStorePath(connectionString);
        if (!useCache)
        {
            services.AddSingleton<IEventStore>(sp =>
                new SqliteEventStore(
                    storePath,
                    sp.GetRequiredService<DcbDomainTypes>().EventTypes,
                    new SqliteEventStoreOptions { AutoCreateDatabase = false },
                    serviceIdProvider: sp.GetRequiredService<IServiceIdProvider>()));
        }
        // The state store always runs its "create if missing" schema step in its constructor (no option to skip it).
        // On a read-only connection that step is a no-op when the schema is current, and fails (without changing the
        // file) when an upgrade would be needed.
        services.AddSingleton<IMultiProjectionStateStore>(sp =>
        {
            try
            {
                return new SqliteMultiProjectionStateStore(storePath, serviceIdProvider: sp.GetRequiredService<IServiceIdProvider>());
            }
            catch (SqliteException ex) when (readOnly)
            {
                throw new CliException(
                    "The SQLite projection state tables need a schema upgrade, which this read-only command does not perform. " +
                    $"Start the ApiService once (it upgrades the schema) and retry. ({ex.Message})");
            }
        });
    }
    else
    {
        // Register Postgres DbContext factory
        services.AddPooledDbContextFactory<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Register event store
        if (!useCache)
        {
            services.AddSingleton<IEventStore, PostgresEventStore>();
        }

        // Register multi projection state store
        services.AddSingleton<IMultiProjectionStateStore, PostgresMultiProjectionStateStore>();
    }

    // Register the builder
    services.AddSingleton<MultiProjectionStateBuilder>();

    // Register individual interfaces from DcbDomainTypes for Service DI
    services.AddSingleton(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
    services.AddSingleton(sp => sp.GetRequiredService<DcbDomainTypes>().TagTypes);
    services.AddSingleton(sp => sp.GetRequiredService<DcbDomainTypes>().TagProjectorTypes);
    services.AddSingleton(sp => sp.GetRequiredService<DcbDomainTypes>().JsonSerializerOptions);

    // Register CLI services
    services.AddSingleton<TagEventService>();
    services.AddSingleton<TagStateService>();
    services.AddSingleton<TagListService>();

    return services.BuildServiceProvider();
}

static async Task<int> ListTagsAsync(
    ConnectionTarget target,
    string? tagGroup,
    string outputDir,
    CacheOptions cacheOptions)
{
    Console.WriteLine("=== Tag List ===\n");
    PrintTarget(target);
    if (!string.IsNullOrEmpty(tagGroup))
    {
        Console.WriteLine($"Filter: {tagGroup}");
    }
    Console.WriteLine($"Output Directory: {outputDir}");
    if (!TryReportCacheUsage(cacheOptions, out var cachePath))
    {
        return 1;
    }
    Console.WriteLine();

    var services = BuildServices(target, cachePath: cachePath);
    var tagListService = services.GetRequiredService<TagListService>();

    // Export tag list
    Directory.CreateDirectory(outputDir);
    var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var fileName = string.IsNullOrEmpty(tagGroup)
        ? $"tag_list_{timestamp}.json"
        : $"tag_list_{ToSafeFileNamePart(tagGroup)}_{timestamp}.json";
    var filePath = Path.Combine(outputDir, fileName);

    var exportResult = await tagListService.ExportTagListAsync(filePath, tagGroup);

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
    return 0;
}

static async Task<int> SyncCacheAsync(ConnectionTarget target, string cacheDir, int safeWindowMinutes, bool verbose, CancellationToken cancellationToken)
{
    Console.WriteLine("=== Cache Sync ===\n");
    PrintTarget(target);
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine($"Safe Window: {safeWindowMinutes} minutes");
    Console.WriteLine();

    // One cache per connection: <cache-dir>/<service id>/<database type>-<connection hash>/events.db
    var cachePath = GetCachePath(cacheDir, target, createKey: true)!;
    var cacheFolder = Path.GetDirectoryName(cachePath)!;
    Directory.CreateDirectory(cacheFolder);
    using var cacheLock = TryAcquireCacheLock(cacheFolder)
        ?? throw new CliException($"Another cache-sync (or cache-clear) is running for this cache: {cacheFolder}");

    // Holding the lock, any temp files left in the folder are from interrupted runs.
    DeleteSyncTempFiles(cacheFolder);

    // Sync into a temporary file and replace events.db only after a complete, successful sync, so a failed or
    // interrupted sync never leaves a partial cache and never destroys the previous good cache.
    var tempPath = Path.Combine(cacheFolder, $"events.db.sync-{Guid.NewGuid():N}.tmp");
    var incremental = File.Exists(cachePath) && GetCacheCompletionStatus(cachePath) is (true, null);
    if (incremental)
    {
        File.Copy(cachePath, tempPath);
    }

    try
    {
        var result = await SyncIntoFileAsync(target, tempPath, existingCache: incremental, safeWindowMinutes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Make the temp file a single self-contained file, then atomically move it over events.db.
        SqliteConnection.ClearAllPools();
        using (var connection = OpenExistingSqlite(tempPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
            command.ExecuteNonQuery();
        }
        DeleteFileIfExists(cachePath + "-wal");
        DeleteFileIfExists(cachePath + "-shm");
        File.Move(tempPath, cachePath, overwrite: true);

        Console.WriteLine($"Sync completed:");
        Console.WriteLine($"  Mode: {(incremental ? "incremental (existing cache)" : "full (new cache)")}");
        Console.WriteLine($"  Action: {result.Action}");
        Console.WriteLine($"  Events synced: {result.EventsSynced:N0}");
        Console.WriteLine($"  Total events in cache: {result.TotalEventsInCache:N0}");
        Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
        Console.WriteLine($"\nCache file: {cachePath}");
        return 0;
    }
    finally
    {
        // Only this run's temp files (other runs cannot hold the lock at the same time, but stay strict anyway).
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            DeleteFileIfExists(tempPath + suffix);
        }
    }
}

static async Task<CacheSyncResult> SyncIntoFileAsync(ConnectionTarget target, string path, bool existingCache, int safeWindowMinutes, CancellationToken cancellationToken)
{
    // Build remote services
    var services = BuildServices(target);
    var remoteStore = services.GetRequiredService<IEventStore>();
    var domainTypes = services.GetRequiredService<DcbDomainTypes>();

    // A copy of an existing cache is opened without schema initialization, which would rewrite the schema version and
    // make the sync discard the cache (full re-download) instead of syncing incrementally.
    var localStore = new SqliteEventStore(
        path,
        domainTypes.EventTypes,
        new SqliteEventStoreOptions { AutoCreateDatabase = !existingCache },
        serviceIdProvider: CreateServiceIdProvider());

    // Cache metadata records a redacted description of the source (no passwords or keys)
    var syncOptions = new CacheSyncOptions
    {
        SafeWindow = TimeSpan.FromMinutes(safeWindowMinutes),
        RemoteEndpoint = DescribeConnection(target),
        DatabaseName = target.DatabaseType == "cosmos" ? target.CosmosDatabaseName : target.DatabaseType
    };
    var cacheSync = SekibanDcbSqliteExtensions.CreateCacheSync(localStore, remoteStore, syncOptions);

    // Perform sync
    Console.WriteLine("Syncing events from remote to local cache...\n");

    var result = await cacheSync.SyncAsync(cancellationToken);
    if (!result.IsSuccess)
    {
        throw new CliException($"Cache sync failed: {result.ErrorMessage}");
    }

    // The library writes metadata only when it cached events; write it for every successful sync.
    if (await localStore.GetMetadataAsync() is null)
    {
        var now = DateTime.UtcNow;
        await localStore.SetMetadataAsync(new CacheMetadata
        {
            RemoteEndpoint = syncOptions.RemoteEndpoint,
            DatabaseName = syncOptions.DatabaseName,
            SchemaVersion = syncOptions.SchemaVersion,
            TotalCountAtFetch = result.TotalEventsInCache,
            CreatedUtc = now,
            UpdatedUtc = now
        });
    }

    // CLI-owned completion marker, written only after a successful sync.
    using (var connection = OpenExistingSqlite(path))
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO dcb_meta (Key, Value) VALUES ($key, $value)";
        command.Parameters.AddWithValue("$key", CacheCompletionKey);
        command.Parameters.AddWithValue("$value", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
    return result;
}

static void DeleteFileIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

// Leftovers of failed or interrupted syncs (events.db.sync-*.tmp and their -wal/-shm/-journal files).
static void DeleteSyncTempFiles(string cacheFolder)
{
    if (!Directory.Exists(cacheFolder))
    {
        return;
    }
    foreach (var file in Directory.GetFiles(cacheFolder, "events.db.sync-*.tmp*"))
    {
        File.Delete(file);
    }
}

// CLI-owned cache folders <root>/<database>-<16 hex>
static List<string> GetCacheFolders(string cacheDir)
{
    var cacheRoot = GetCacheRoot(cacheDir);
    var cacheFolderPattern = new Regex("^(postgres|cosmos|sqlite)-[0-9a-f]{16}$");
    return Directory.Exists(cacheRoot)
        ? Directory.GetDirectories(cacheRoot).Where(dir => cacheFolderPattern.IsMatch(Path.GetFileName(dir))).ToList()
        : [];
}

// Cache files of this CLI only: <root>/<database>-<16 hex>/events.db, plus the legacy <root>/events.db when it
// carries cache-sync metadata. Other SQLite files (for example an ApiService events.db) are never picked up.
static List<CacheFile> FindCacheFiles(string cacheDir)
{
    var cacheRoot = GetCacheRoot(cacheDir);
    if (!Directory.Exists(cacheRoot))
    {
        return [];
    }

    // <root>/<database>-<16 hex>/events.db: CLI-owned cache folders (listed even when unreadable)
    var files = GetCacheFolders(cacheDir)
        .Select(dir => Path.Combine(dir, "events.db"))
        .Where(File.Exists)
        .Select(path => new CacheFile(path, IsLegacy: false))
        .ToList();

    // Legacy <root>/events.db: only a candidate; see GetCacheSyncStatus
    var legacyPath = Path.Combine(cacheRoot, "events.db");
    if (File.Exists(legacyPath))
    {
        files.Add(new CacheFile(legacyPath, IsLegacy: true));
    }
    return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
}

// Legacy <root>/events.db (older CLI versions): a cache only when cache-sync wrote metadata to it (read-only check).
// IsCache is false for any other file; Error is set when the file or its metadata cannot be read.
static (bool IsCache, string? Error) GetCacheSyncStatus(string path) =>
    QueryCacheMeta(path, "Key = 'remoteEndpoint' OR Key LIKE '%:remoteEndpoint'");

// A cache folder file is complete only when a successful cache-sync of this CLI wrote the completion marker.
static (bool IsComplete, string? Error) GetCacheCompletionStatus(string path) =>
    QueryCacheMeta(path, $"Key = '{CacheCompletionKey}'");

static (bool Found, string? Error) QueryCacheMeta(string path, string keyCondition)
{
    try
    {
        if (!SqliteTableExists(path, "dcb_events") || !SqliteTableExists(path, "dcb_meta"))
        {
            return (false, null);
        }
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dcb_meta WHERE {keyCondition}";
        return (Convert.ToInt64(command.ExecuteScalar()) > 0, null);
    }
    catch (SqliteException ex)
    {
        return (false, ex.Message);
    }
}

static async Task<int> ShowCacheStatsAsync(string cacheDir)
{
    Console.WriteLine("=== Cache Statistics ===\n");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine();

    var cacheFiles = FindCacheFiles(cacheDir);
    if (cacheFiles.Count == 0)
    {
        Console.WriteLine("No cache file found.");
        Console.WriteLine($"Expected location: {GetCacheRoot(cacheDir)}/<database>-<connection hash>/events.db");
        return 0;
    }

    var domainTypes = DomainType.GetDomainTypes();
    var failures = 0;
    var shown = 0;
    foreach (var (cachePath, isLegacy) in cacheFiles)
    {
        // Cache folders: complete when the CLI completion marker exists; legacy root file: cache-sync metadata
        var (isCache, statusError) = isLegacy ? GetCacheSyncStatus(cachePath) : GetCacheCompletionStatus(cachePath);
        if (isLegacy)
        {
            if (statusError != null)
            {
                Console.Error.WriteLine($"  Error: Could not read legacy cache {cachePath}: {statusError}");
                failures++;
                continue;
            }
            if (!isCache)
            {
                continue;
            }
        }
        shown++;

        // Get file info
        var fileInfo = new FileInfo(cachePath);
        Console.WriteLine($"Cache File: {cachePath}");
        Console.WriteLine($"File Size: {FormatBytes(fileInfo.Length)}");
        Console.WriteLine($"Last Modified: {fileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");

        // Open the cache read-only without schema initialization. Caches are CLI-owned, so a path that cannot be
        // expressed read-only is opened normally (still without schema initialization).
        var localStore = new SqliteEventStore(
            TryToReadOnlySqlitePath(cachePath) ?? cachePath,
            domainTypes.EventTypes,
            new SqliteEventStoreOptions { AutoCreateDatabase = false },
            serviceIdProvider: CreateServiceIdProvider());

        // Get event count
        try
        {
            Console.WriteLine($"Total Events: {await localStore.GetEventCountAsync():N0}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error: Could not count events in {cachePath}: {ex.Message}");
            failures++;
        }

        // Get metadata (the stored source is shown through the allowlist; legacy caches stored raw text).
        // GetMetadataAsync hides read errors, so the read-only status check above decides whether it must exist.
        if (statusError != null)
        {
            Console.Error.WriteLine($"  Error: Could not read cache metadata of {cachePath}: {statusError}");
            failures++;
        }
        else if (!isCache)
        {
            Console.Error.WriteLine($"  Error: {cachePath} has no completion marker (incomplete or old sync); it is not used.");
            failures++;
        }
        var metadata = statusError == null && isCache ? await localStore.GetMetadataAsync() : null;
        if (statusError == null && isCache && metadata == null)
        {
            Console.Error.WriteLine($"  Error: Could not read cache metadata of {cachePath}.");
            failures++;
        }
        if (metadata != null)
        {
            Console.WriteLine("Cache Metadata:");
            Console.WriteLine($"  Source: {ConnectionIdentity.DescribeStoredSource(metadata.RemoteEndpoint)}");
            Console.WriteLine($"  Database Name: {ConnectionIdentity.DescribeStoredSource(metadata.DatabaseName)}");
            Console.WriteLine($"  Last Sync: {metadata.UpdatedUtc:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"  Last SortableUniqueId: {metadata.LastCachedSortableUniqueId}");
            Console.WriteLine($"  Schema Version: {metadata.SchemaVersion}");
        }

        // Get tag summary
        try
        {
            var tags = (await localStore.GetAllTagsAsync()).ToList();
            var tagGroups = tags.GroupBy(t => t.TagGroup).ToList();

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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error: Could not read tags in {cachePath}: {ex.Message}");
            failures++;
        }
        Console.WriteLine();
    }

    if (shown == 0 && failures == 0)
    {
        Console.WriteLine("No cache file found.");
    }
    if (failures > 0)
    {
        throw new CliException($"{failures} cache read(s) failed.");
    }
    return 0;
}

static Task<int> ClearCacheAsync(string cacheDir, bool assumeYes)
{
    Console.WriteLine("=== Clear Cache ===\n");
    Console.WriteLine($"Cache Directory: {cacheDir}");
    Console.WriteLine();

    // Cache folders are CLI-owned: events.db (even when unreadable) and leftover sync temp files are deleted while
    // holding the folder's sync lock; a folder whose lock is held by a running cache-sync is skipped. A legacy
    // <root>/events.db is deleted only when its metadata identifies it as a cache; an unreadable one is kept.
    var folderFiles = GetCacheFolders(cacheDir)
        .Select(folder => (Folder: folder, Files: Directory.GetFiles(folder, "events.db")
            .Concat(Directory.GetFiles(folder, "events.db.sync-*.tmp*")).ToList()))
        .Where(entry => entry.Files.Count > 0)
        .ToList();

    string? legacyToDelete = null;
    var unreadableLegacy = 0;
    foreach (var (path, _) in FindCacheFiles(cacheDir).Where(file => file.IsLegacy))
    {
        var (isCache, error) = GetCacheSyncStatus(path);
        if (error != null)
        {
            Console.Error.WriteLine($"  Error: Could not read legacy cache {path} (not deleted): {error}");
            unreadableLegacy++;
        }
        else if (isCache)
        {
            legacyToDelete = path;
        }
    }

    var fileCount = folderFiles.Sum(entry => entry.Files.Count) + (legacyToDelete != null ? 1 : 0);
    if (fileCount == 0)
    {
        Console.WriteLine("No cache file found. Nothing to clear.");
        return unreadableLegacy > 0
            ? throw new CliException($"{unreadableLegacy} legacy cache file(s) could not be read and were kept.")
            : Task.FromResult(0);
    }

    foreach (var file in folderFiles.SelectMany(entry => entry.Files).Append(legacyToDelete).OfType<string>())
    {
        Console.WriteLine($"Cache file: {file}");
    }
    Console.WriteLine();

    if (!Confirm($"Are you sure you want to delete {fileCount} cache file(s)? (y/N): ", assumeYes))
    {
        Console.WriteLine("Cancelled.");
        return Task.FromResult(0);
    }

    var skipped = 0;
    foreach (var (folder, _) in folderFiles)
    {
        using var folderLock = TryAcquireCacheLock(folder);
        if (folderLock == null)
        {
            Console.Error.WriteLine($"  Skipped {folder}: a cache-sync is running for it.");
            skipped++;
            continue;
        }
        DeleteCacheFiles(Path.Combine(folder, "events.db"));
        DeleteSyncTempFiles(folder);
    }
    if (legacyToDelete != null)
    {
        DeleteCacheFiles(legacyToDelete);
    }

    Console.WriteLine(skipped == 0 ? "\nCache cleared successfully." : "\nCache partially cleared.");
    if (unreadableLegacy > 0 || skipped > 0)
    {
        var problems = new List<string>();
        if (unreadableLegacy > 0)
        {
            problems.Add($"{unreadableLegacy} legacy cache file(s) could not be read and were kept");
        }
        if (skipped > 0)
        {
            problems.Add($"{skipped} cache folder(s) were skipped because a cache-sync is running");
        }
        throw new CliException(string.Join("; ", problems) + ".");
    }
    return Task.FromResult(0);
}

// A cache file found under the cache root; IsLegacy for the old <root>/events.db location.
readonly record struct CacheFile(string Path, bool IsLegacy);

// The resolved store a command talks to. For sqlite, ConnectionString is the database file path.
readonly record struct ConnectionTarget(string DatabaseType, string ConnectionString, string CosmosDatabaseName);

// Expected command failure: the message is printed to stderr and the CLI exits with code 1.
sealed class CliException(string message) : Exception(message);
