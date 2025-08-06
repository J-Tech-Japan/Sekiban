# Sekiban.Dcb.Postgres.Tests

Integration tests for the PostgreSQL implementation of Sekiban DCB using in-memory actors.

## Overview

These tests verify that:
- Events are correctly persisted to PostgreSQL
- Tags are properly tracked in the database
- In-memory actors work correctly with PostgreSQL storage
- Event ordering by SortableUniqueId is maintained
- Actor state can be recreated from database after removal

## Running Tests

### Option 1: Using Testcontainers (Recommended)

The tests use Testcontainers to automatically manage PostgreSQL containers. Simply run:

```bash
dotnet test tests/Sekiban.Dcb.Postgres.Tests
```

Requirements:
- Docker must be installed and running
- The tests will automatically start and stop PostgreSQL containers

### Option 2: Using Docker Compose

If you prefer to manage the database manually:

1. Start the test database:
```bash
cd tests/Sekiban.Dcb.Postgres.Tests
docker-compose up -d
```

2. Modify the connection string in `PostgresTestFixture.cs` if needed

3. Run the tests:
```bash
dotnet test
```

4. Stop the database:
```bash
docker-compose down
```

## Test Structure

- **PostgresTestFixture**: Manages database lifecycle and service setup
- **PostgresTestBase**: Base class that ensures clean database for each test
- **PostgresTagBasedEventTests**: Tests for tag-based event operations
- **PostgresWithActorsTests**: Tests for in-memory actors with PostgreSQL storage

## Database Cleanup

The database is automatically cleared between tests using `TRUNCATE` commands, ensuring test isolation without recreating the schema.

## Key Features Tested

1. **Event Storage**: Writing and reading events with proper ordering
2. **Tag Tracking**: Tag-to-event relationships via SortableUniqueId
3. **Actor Integration**: In-memory actors persisting to PostgreSQL
4. **Concurrency**: Handling concurrent commands with actor consistency
5. **State Recreation**: Actors can catch up from database after removal