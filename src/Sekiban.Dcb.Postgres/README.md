# Sekiban.Dcb.Postgres

PostgreSQL implementation of the Sekiban DCB (Domain-Command-Business) event store.

## Features

- **Event Storage**: Events are stored in the `dcb_events` table with JSONB payload
- **Tag Tracking**: Tags are tracked in the `dcb_tags` table, linking tags to events via SortableUniqueId
- **Optimized Ordering**: Both tables are indexed and sorted by SortableUniqueId for efficient chronological queries
- **No State Storage**: Tag states are not persisted; they should be computed by projectors when needed

## Database Schema

### Events Table (`dcb_events`)
- `Id` (Guid) - Primary key
- `SortableUniqueId` (string) - Unique, indexed for ordering
- `EventType` (string) - Type of the event
- `Payload` (JSONB) - Serialized event payload
- `Tags` (JSONB) - Array of associated tags
- `Timestamp` (DateTime)
- Event metadata fields (CausationId, CorrelationId, ExecutedUser)

### Tags Table (`dcb_tags`)
- `Id` (long) - Auto-increment primary key
- `Tag` (string) - The tag identifier (e.g., "Student:123")
- `SortableUniqueId` (string) - Links to the event's SortableUniqueId
- `EventId` (Guid) - The associated event ID
- `CreatedAt` (DateTime)

## Usage

### Configuration

Add to your service configuration:

```csharp
services.AddSekibanDcbPostgres(configuration);
// or with explicit connection string
services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

### Running Migrations

Use the MigrationHost project to apply database migrations:

```bash
dotnet run --project src/Sekiban.Dcb.Postgres.MigrationHost
```

Or configure the connection string in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SekibanDcbConnection": "Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres"
  }
}
```

## Key Design Decisions

1. **SortableUniqueId Ordering**: All queries are ordered by SortableUniqueId to maintain chronological consistency
2. **JSONB Storage**: Uses PostgreSQL's JSONB type for flexible payload storage with query capabilities
3. **Simplified Tag Table**: Tags table only tracks relationships, not state - state computation is delegated to projectors
4. **Indexed Queries**: Strategic indexes on Tag, SortableUniqueId, and composite (Tag, SortableUniqueId) for efficient querying