# DcbOrleans CLI (LLM Notes)

This CLI is a maintenance tool for projections, tags, and events in a Sekiban DCB database.
It supports PostgreSQL and Cosmos DB.

## Quick Start

Run from the CLI project directory:

```bash
dotnet run -- --help
```

## Configuration

You can configure connection settings in one of three ways:

1) Command line options

```bash
dotnet run -- status -d postgres -c "Host=localhost;Database=...;Username=...;Password=..."
```

2) Environment variables

```bash
export DATABASE_TYPE="postgres"
export CONNECTION_STRING="Host=localhost;Database=...;Username=...;Password=..."
export COSMOS_CONNECTION_STRING="AccountEndpoint=...;AccountKey=..."
export COSMOS_DATABASE_NAME="SekibanDcb"
export OUTPUT_DIR="./output"
```

3) User secrets (recommended for local dev)

```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DcbPostgres" "Host=localhost;Database=...;Username=...;Password=..."
dotnet user-secrets set "ConnectionStrings:SekibanDcbCosmos" "AccountEndpoint=...;AccountKey=..."
dotnet user-secrets set "Sekiban:Database" "postgres"
dotnet user-secrets set "CosmosDb:DatabaseName" "SekibanDcb"
```

## Common Commands

- `list` : List registered projectors
- `status` : Show projection state status
- `build` : Build multi-projection state(s)
- `save` : Save projection state JSON to files
- `delete` : Delete projection state(s)
- `tag-events` : Export all events for a tag
- `projection` : Show a projection state
- `tag-state` : Project a tag state (auto or explicit projector)
- `tag-list` : Export list of tags
- `cache-update` : Update SQLite event cache (SafeWindow: now - 10 minutes)
- `cache-sync` : Sync remote events to local SQLite cache (legacy)
- `cache-stats` : Show local cache statistics
- `cache-clear` : Clear local SQLite cache

## Key Options

- `-d, --database` : `postgres` or `cosmos`
- `-c, --connection-string` : PostgreSQL connection string
- `--cosmos-connection-string` : Cosmos DB connection string
- `--cosmos-database` : Cosmos database name
- `--cache-mode` : Cache mode `auto` | `off` | `clear` | `cache-only` (default `auto`)
- `-p, --projector` : Target projector name
- `-o, --output-dir` : Output directory (default `./output`)
- `-t, --tag` : Tag in `group:content` format
- `-P, --tag-projector` : Tag projector name (auto-detects if not specified)
- `-m, --min-events` : Min events before build (default from `MIN_EVENTS` or 3000)
- `-f, --force` : Force rebuild
- `-v, --verbose` : Verbose output

## Examples

```bash
# List projectors
dotnet run -- list

# Show status (uses config/env/user-secrets)
dotnet run -- status

# Build all projection states
dotnet run -- build

# Build a specific projector
dotnet run -- build -p "WeatherForecastProjector"

# Export tag events
dotnet run -- tag-events -t "WeatherForecast:00000000-0000-0000-0000-000000000001"

# Project a tag state (auto projector resolution)
dotnet run -- tag-state -t "WeatherForecast:00000000-0000-0000-0000-000000000001"

# Explicit projector
dotnet run -- tag-state -t "WeatherForecast:00000000-0000-0000-0000-000000000001" -P "WeatherForecastProjector"

# Update cache (profile-based path: ./output/cache/{profile})
dotnet run -- cache-update --profile stg

# Build using cache (auto uses cache if present)
dotnet run -- build --profile stg --cache-mode auto
```

## Notes

- Output files are written to `./output` unless overridden with `--output-dir`.
- Use `dotnet run -- <command> --help` to see command-specific options.
- Cache path for `cache-update` and `--cache-mode` is fixed to `./output/cache/{profile}`.
