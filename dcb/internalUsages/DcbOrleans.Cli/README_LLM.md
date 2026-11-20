# DcbOrleans CLI (LLM Notes)

This CLI is a maintenance tool for projections, tags, and events in a Sekiban DCB database.
It supports PostgreSQL, Cosmos DB and SQLite (the `events.db` file the ApiService writes when it runs with `Sekiban:Database=sqlite`).

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
export SQLITE_PATH="../DcbOrleans.ApiService/events.db"
export OUTPUT_DIR="./output"
```

3) User secrets (recommended for local dev; the project already declares a `UserSecretsId`)

```bash
dotnet user-secrets set "ConnectionStrings:DcbPostgres" "Host=localhost;Database=...;Username=...;Password=..."
dotnet user-secrets set "ConnectionStrings:SekibanDcbCosmos" "AccountEndpoint=...;AccountKey=..."
dotnet user-secrets set "Sekiban:Database" "postgres"
dotnet user-secrets set "CosmosDb:DatabaseName" "SekibanDcb"
dotnet user-secrets set "Sekiban:SqlitePath" "../DcbOrleans.ApiService/events.db"
```

Profiles let you switch between environments with `--profile` (`-P`):

```bash
dotnet user-secrets set "Profiles:dev:Database" "postgres"
dotnet user-secrets set "Profiles:dev:ConnectionString" "Host=localhost;Database=...;Username=...;Password=..."
dotnet user-secrets set "Profiles:local:Database" "sqlite"
dotnet user-secrets set "Profiles:local:SqlitePath" "../DcbOrleans.ApiService/events.db"
dotnet user-secrets set "DefaultProfile" "dev"
dotnet run -- profiles
dotnet run -- status -P local
```

## Common Commands

- `list` : List registered projectors
- `status` : Show projection state status
- `build` : Build multi-projection state(s)
- `save` : Save projection state JSON to files
- `delete` : Delete projection state(s) (asks for confirmation; `--yes` skips it)
- `tag-events` : Export all events for a tag
- `projection` : Show a projection state
- `tag-state` : Project a tag state (auto or explicit projector)
- `tag-list` : Export list of tags
- `cache-sync` : Sync remote events to the local SQLite cache of the selected connection
- `cache-stats` : Show statistics of every cache under the cache directory
- `cache-clear` : Delete every cache under the cache directory (asks for confirmation; `--yes` skips it)

## Key Options

- `-P, --profile` : Profile name from user secrets (`Profiles:<name>:...`)
- `-d, --database` : `postgres`, `cosmos` or `sqlite`
- `-c, --connection-string` : PostgreSQL connection string
- `--cosmos-connection-string` : Cosmos DB connection string
- `--cosmos-database` : Cosmos database name
- `--sqlite-path` : SQLite event store file (for `-d sqlite`). It must already contain the `dcb_events` table. All commands except `build` and `delete` open it read-only and never create, migrate or switch it to WAL. The path must not contain `;`, `=` or quotes for any command, because the Sekiban SQLite stores put it into a connection string
- `-p, --projector` : Projector name. For `build` it must be a registered multi-projector (see `list`); `save` / `delete` / `projection` also accept a name that only exists in stored states (e.g. a renamed projector); for `tag-state` it is a tag projector and is auto-detected from the tag group when omitted
- `-o, --output-dir` : Output directory (default `./output`)
- `-t, --tag` : Tag in `group:content` format
- `-m, --min-events` : Min events before build (default from `MIN_EVENTS` or 3000)
- `-f, --force` : Force rebuild
- `-v, --verbose` : Verbose output
- `--cache-mode` : `auto` | `off` | `clear` | `cache-only` (default `auto`)
- `-C, --cache-dir` : Cache directory (default `./cache`)
- `--safe-window` : Safe window minutes for `cache-sync`
- `-y, --yes` : Skip the confirmation prompt of `delete` / `cache-clear` (required when stdin is not interactive)

## Examples

```bash
# List projectors
dotnet run -- list

# Show status (uses config/env/user-secrets)
dotnet run -- status

# Build all projection states
dotnet run -- build

# Build a specific projector
dotnet run -- build -p "WeatherForecastProjection"

# Export tag events
dotnet run -- tag-events -t "WeatherForecast:00000000-0000-0000-0000-000000000001"

# Project a tag state (auto projector resolution)
dotnet run -- tag-state -t "WeatherForecast:00000000-0000-0000-0000-000000000001"

# Explicit projector
dotnet run -- tag-state -t "WeatherForecast:00000000-0000-0000-0000-000000000001" -p "WeatherForecastProjector"

# Delete one projector's states without a prompt (scripts / CI)
dotnet run -- delete -p "WeatherForecastProjection" --yes

# Read the SQLite event store of a locally running ApiService
dotnet run -- status -d sqlite --sqlite-path "../DcbOrleans.ApiService/events.db"

# Sync cache and use it for tag events
dotnet run -- cache-sync --cache-dir ./cache --safe-window 10
dotnet run -- tag-events -t "WeatherForecast:00000000-0000-0000-0000-000000000001" --cache-mode auto --cache-dir ./cache
```

## Notes

- Output files are written to `./output` unless overridden with `--output-dir`. `save` writes `<projector>_v<version>_<timestamp>_metadata.json` and `_state.json` per projector version.
- Exit code is `0` on success and `1` on failure (connection error, unknown projector or tag projector, invalid option value, failed build). Errors are written to stderr.
- Caches are kept per connection: `<cache-dir>/<database>-<hash>/events.db`. The folder name is an HMAC-SHA256 (random key in `.cache-key` in the cache root, created once) of the database type, the full connection string, the Cosmos database name or SQLite path and, for PostgreSQL, the `PG*` environment variables (`PGOPTIONS`, `PGUSER`, `PGPASSWORD`, `PGHOST`, ...). Any difference, including a password change, gives a separate cache; the folder name reveals nothing about the connection. Output and cache metadata only show host, port, database, username, search path and account endpoint; passwords, keys, tokens and `Options` are never shown. `cache-sync` syncs into a temporary file (a copy of the existing cache, so a re-sync downloads only new events) and replaces `events.db` only after a successful sync and a completion marker; a failed or interrupted sync leaves the previous cache untouched, and leftovers are removed by the next `cache-sync` / `cache-clear`. A cache without the completion marker is not used (`auto` warns, `cache-only` fails, `cache-stats` exits 1). `cache-stats` / `cache-clear` only touch files in this layout, plus a legacy `events.db` directly in the cache root when cache-sync metadata identifies it as a cache (a legacy file that cannot be read is reported and kept; files in the cache folders are deleted even when unreadable). A cache synced from one database is never used for another one.
- When `--cache-mode` is `auto`, the CLI uses the completed cache of the selected connection if it exists, otherwise it reads the database directly.
- `cache-only` requires an existing cache for the same connection (run `cache-sync` first).
- Use `dotnet run -- <command> --help` to see command-specific options.
