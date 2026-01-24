# CLI Tool - Sekiban DCB

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Custom MultiProjector Serialization](17_custom_multiprojector_serialization.md)
> - [CLI Tool](18_cli_tool.md) (You are here)

## Overview

Sekiban DCB provides a command-line interface (CLI) tool for managing projection states, inspecting events, and working with local SQLite caches. The CLI is particularly useful for:

- Building and rebuilding multi-projection states
- Debugging by inspecting events and projection states
- Managing local SQLite cache for offline development
- Working with multiple environments via profiles

## Project Setup

### Creating a CLI Project

Create a new console application and add the necessary references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFrameworks>net10.0;net9.0</TargetFrameworks>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <UserSecretsId>your-cli-secrets-id</UserSecretsId>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="9.0.1" />
        <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.1" />
        <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
    </ItemGroup>

    <ItemGroup>
        <!-- Your domain project -->
        <ProjectReference Include="..\YourApp.Domain\YourApp.Domain.csproj"/>

        <!-- Sekiban DCB packages -->
        <ProjectReference Include="..\..\src\Sekiban.Dcb.WithResult\Sekiban.Dcb.WithResult.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.Postgres\Sekiban.Dcb.Postgres.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.CosmosDb\Sekiban.Dcb.CosmosDb.csproj"/>
        <ProjectReference Include="..\..\src\Sekiban.Dcb.Sqlite\Sekiban.Dcb.Sqlite.csproj"/>
    </ItemGroup>
</Project>
```

### Basic Program Structure

```csharp
using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Sqlite.Services;
using Sekiban.Dcb.Storage;
using YourApp.Domain;

// Build configuration
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();

// Create root command
var rootCommand = new RootCommand("Your CLI Tool - Manage projections and events");

// Add commands...
rootCommand.AddCommand(buildCommand);
rootCommand.AddCommand(statusCommand);
// etc.

return await rootCommand.InvokeAsync(args);
```

## Multi-Profile Configuration

The CLI supports multiple profiles for connecting to different environments (dev, staging, production).

### Setting Up Profiles with User Secrets

```bash
# Initialize user secrets
dotnet user-secrets init

# Set up a "dev" profile
dotnet user-secrets set "Profiles:dev:Database" "postgres"
dotnet user-secrets set "Profiles:dev:ConnectionString" "Host=localhost;Database=sekiban;..."

# Set up a "staging" profile with Cosmos DB
dotnet user-secrets set "Profiles:stg:Database" "cosmos"
dotnet user-secrets set "Profiles:stg:CosmosConnectionString" "AccountEndpoint=https://...;AccountKey=...;"
dotnet user-secrets set "Profiles:stg:CosmosDatabase" "SekibanDcb"

# Set default profile
dotnet user-secrets set "DefaultProfile" "dev"
```

### Profile Configuration Structure

```json
{
  "Profiles": {
    "dev": {
      "Database": "postgres",
      "ConnectionString": "Host=localhost;Database=sekiban;Username=..."
    },
    "stg": {
      "Database": "cosmos",
      "CosmosConnectionString": "AccountEndpoint=https://...;AccountKey=...;",
      "CosmosDatabase": "SekibanDcbStaging"
    },
    "prod": {
      "Database": "cosmos",
      "CosmosConnectionString": "AccountEndpoint=https://...;AccountKey=...;",
      "CosmosDatabase": "SekibanDcbProd"
    }
  },
  "DefaultProfile": "dev"
}
```

### Profile Alias Keys

The following aliases are supported for convenience:

| Alias Key | Maps To |
|-----------|---------|
| `Database` | `Sekiban:Database` |
| `ConnectionString` | `ConnectionStrings:DcbPostgres` |
| `PostgresConnectionString` | `ConnectionStrings:DcbPostgres` |
| `CosmosConnectionString` | `ConnectionStrings:SekibanDcbCosmos` |
| `CosmosDatabase` | `CosmosDb:DatabaseName` |

### Implementing Profile Resolution

```csharp
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
            return configuration;
        }
    }

    if (!hasProfiles)
    {
        Console.WriteLine($"Error: Profile '{resolvedProfile}' is not defined.");
        return null;
    }

    var profileSection = profilesSection.GetSection(resolvedProfile!);
    if (!profileSection.Exists())
    {
        Console.WriteLine($"Error: Profile '{resolvedProfile}' not found.");
        return null;
    }

    // Build configuration with profile overrides
    var prefix = $"Profiles:{resolvedProfile}:";
    var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    foreach (var pair in profileSection.AsEnumerable())
    {
        if (pair.Value == null) continue;
        var key = pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? pair.Key[prefix.Length..]
            : pair.Key;
        overrides[key] = pair.Value;
    }

    // Map aliases
    void MapAlias(string aliasKey, string targetKey)
    {
        if (overrides.TryGetValue(aliasKey, out var value) && !overrides.ContainsKey(targetKey))
            overrides[targetKey] = value;
    }

    MapAlias("Database", "Sekiban:Database");
    MapAlias("CosmosConnectionString", "ConnectionStrings:SekibanDcbCosmos");
    MapAlias("CosmosDatabase", "CosmosDb:DatabaseName");
    MapAlias("ConnectionString", "ConnectionStrings:DcbPostgres");

    return new ConfigurationBuilder()
        .AddConfiguration(configuration)
        .AddInMemoryCollection(overrides!)
        .Build();
}
```

## Available Commands

### profiles

List all available profiles:

```bash
dotnet run -- profiles
```

Output:
```
=== Available Profiles ===

  - dev (default)
  - stg
  - prod
```

### status

Show the status of all projection states:

```bash
dotnet run -- status --profile dev
```

Options:
- `--profile, -P` - Profile name
- `--database, -d` - Database type (postgres/cosmos)
- `--connection-string, -c` - PostgreSQL connection string
- `--cosmos-connection-string` - Cosmos DB connection string
- `--cosmos-database` - Cosmos DB database name

### build

Build or rebuild multi-projection states:

```bash
dotnet run -- build --profile dev --force --verbose
```

Options:
- `--profile, -P` - Profile name
- `--min-events, -m` - Minimum events before building (default: 3000)
- `--projector, -p` - Specific projector to build
- `--force, -f` - Force rebuild even if state exists
- `--verbose, -v` - Show verbose output

### save

Export projection state JSON to files:

```bash
dotnet run -- save --profile dev --projector MyProjector --output-dir ./output
```

### delete

Delete projection states:

```bash
dotnet run -- delete --profile dev --projector MyProjector
```

### tag-events

Fetch and export all events for a specific tag:

```bash
dotnet run -- tag-events --profile dev --tag "User:12345" --output-dir ./output
```

### tag-state

Project and display the current state for a specific tag:

```bash
dotnet run -- tag-state --profile dev --tag "User:12345" --projector UserProjector
```

### tag-list

List all tags in the event store:

```bash
dotnet run -- tag-list --profile dev --tag-group User --output-dir ./output
```

### projection

Display the current state of a projection:

```bash
dotnet run -- projection --profile dev --projector MyProjector
```

## Local SQLite Cache

The CLI includes support for caching remote events to a local SQLite database for faster development.

### Sekiban.Dcb.Sqlite Package

The `Sekiban.Dcb.Sqlite` package provides:

- `SqliteEventStore` - Full `IEventStore` implementation
- `SqliteMultiProjectionStateStore` - Full `IMultiProjectionStateStore` implementation
- `EventStoreCacheSync` - Helper for syncing remote to local
- CLI services for tag operations

### cache-sync

Sync remote events to local SQLite cache:

```bash
dotnet run -- cache-sync --profile dev --cache-dir ./cache --safe-window 10
```

Options:
- `--cache-dir, -C` - Cache directory (default: ./cache)
- `--safe-window` - Minutes of events to exclude from cache (default: 10)

The safe window prevents caching events that may still be in flight or not yet committed.

### cache-stats

Show local cache statistics:

```bash
dotnet run -- cache-stats --cache-dir ./cache
```

Output:
```
=== Cache Statistics ===

Cache Directory: ./cache

Cache File: ./cache/events.db
File Size: 15.2 MB
Last Modified: 2025-01-17 09:30:45 UTC

Total Events: 42,000

Cache Metadata:
  Remote Endpoint: https://myaccount.documents.azure.com
  Database Name: SekibanDcb
  Last Sync: 2025-01-17 09:30:45 UTC
  Schema Version: 1.0

Tags: 150 total across 5 groups
  User: 100 tags
  Order: 30 tags
  Product: 20 tags
```

### cache-clear

Clear the local cache:

```bash
dotnet run -- cache-clear --cache-dir ./cache
```

## Building Services

```csharp
static IServiceProvider BuildServices(string connectionString, string databaseType, string cosmosDatabaseName)
{
    var services = new ServiceCollection();
    var domainTypes = YourDomainType.GetDomainTypes();

    services.AddSingleton(domainTypes);

    if (databaseType.ToLowerInvariant() == "cosmos")
    {
        services.AddSingleton<IEventStore>(sp =>
        {
            var client = new CosmosClient(connectionString);
            return new CosmosEventStore(client, cosmosDatabaseName, domainTypes);
        });
        services.AddSingleton<IMultiProjectionStateStore>(sp =>
        {
            var client = new CosmosClient(connectionString);
            return new CosmosMultiProjectionStateStore(client, cosmosDatabaseName, domainTypes);
        });
    }
    else
    {
        services.AddSekibanDcbPostgres(connectionString);
    }

    // Add CLI services
    services.AddSekibanDcbCliServices();

    return services.BuildServiceProvider();
}
```

## Usage Examples

```bash
# List profiles
dotnet run --framework net9.0 -- profiles

# Check status with default profile
dotnet run --framework net9.0 -- status

# Check status with specific profile
dotnet run --framework net9.0 -- status --profile prod

# Build projections with force rebuild
dotnet run --framework net9.0 -- build --profile dev -f -v

# Sync to local cache
dotnet run --framework net9.0 -- cache-sync --profile prod

# Work offline with local cache
dotnet run --framework net9.0 -- tag-list -d sqlite -c "./cache/events.db"
```

## Reference Implementation

For a complete reference implementation, see:
- `dcb/internalUsages/DcbOrleans.Cli/Program.cs`

This implementation includes all commands, profile support, and cache management features.
