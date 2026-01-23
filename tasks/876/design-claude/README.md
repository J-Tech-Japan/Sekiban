# Sekiban.Dcb.DynamoDB Design Document

## Issue #876: Add DynamoDB Support for Sekiban.Dcb

**Author**: Claude (Design Document)
**Date**: 2026-01-22
**Status**: Approved for Implementation

**Implementation Decisions**: See [IMPLEMENTATION_DECISIONS.md](./IMPLEMENTATION_DECISIONS.md)

---

## 1. Overview

This document outlines the design for implementing Amazon DynamoDB support in Sekiban.Dcb, following the same architectural patterns established in the existing Cosmos DB implementation.

### 1.1 Goals

- Provide a DynamoDB event store implementation for Sekiban.Dcb
- Maintain API parity with the Cosmos DB provider
- Leverage DynamoDB-specific features for optimal performance
- Support both single-region and global deployments

### 1.2 Non-Goals

- Migration tools from Cosmos DB to DynamoDB
- Multi-region active-active replication (future enhancement)

### 1.3 Related Packages

This implementation requires an additional package for large state offloading:

| Package | Purpose |
|---------|---------|
| `Sekiban.Dcb.DynamoDB` | Event store implementation (this document) |
| `Sekiban.Dcb.BlobStorage.S3` | S3 snapshot offloading for projection states > 400KB |

See [S3_OFFLOAD_PACKAGE.md](./S3_OFFLOAD_PACKAGE.md) for the S3 package design.

---

## 2. DynamoDB Service Limits and Constraints

Understanding DynamoDB limits is critical for a correct implementation.

### 2.1 Item and Transaction Limits

| Limit | Value | Impact on Design |
|-------|-------|------------------|
| **Item size** | 400 KB | Projection states must offload to S3 when exceeded |
| **TransactWriteItems** | 100 items max | Batch writes must be chunked |
| **TransactWriteItems size** | 4 MB total | Large payloads need chunking |
| **BatchWriteItem** | 25 items max | Used for non-transactional bulk writes |
| **BatchGetItem** | 100 items max, 16 MB | Pagination required for large reads |
| **Partition throughput** | 3000 RCU / 1000 WCU | Hot partition mitigation needed |

### 2.2 Consistency Constraints

| Operation Type | Consistency Available |
|----------------|----------------------|
| GetItem / Query (table) | Strong or Eventually consistent |
| Query (GSI) | **Eventually consistent only** |
| Scan | Strong or Eventually consistent |
| TransactGetItems | Serializable |

**Critical**: GSI queries are always eventually consistent. This affects `ReadAllEventsAsync` which uses GSI1.

### 2.3 Design Implications

1. **Projection State Offloading (Mandatory)**
   - States > 350KB should offload to S3 (leaving margin for metadata)
   - `Sekiban.Dcb.BlobStorage.S3` package is required

2. **Transaction Batching**
   - Events + Tags must fit within 100 items per transaction
   - Multiple transactions require idempotency tokens

3. **GSI Hot Partition**
   - `ALL_EVENTS` partition can become hot under high write load
   - Optional write sharding distributes load

---

## 3. Architecture

### 2.1 Project Structure

```
dcb/src/Sekiban.Dcb.DynamoDB/
├── DynamoDbEventStore.cs              # IEventStore implementation
├── DynamoDbContext.cs                 # Table management
├── DynamoDbInitializer.cs             # Initialization hosted service
├── DynamoDbEventStoreOptions.cs       # Configuration options
├── DynamoMultiProjectionStateStore.cs # Projection state management
├── SekibanDcbDynamoDbExtensions.cs    # DI registration
├── Models/
│   ├── DynamoEvent.cs                 # Event storage model
│   ├── DynamoTag.cs                   # Tag reference model
│   └── DynamoMultiProjectionState.cs  # Projection state model
└── Sekiban.Dcb.DynamoDB.csproj
```

### 2.2 Dependencies

```xml
<PackageReference Include="AWSSDK.DynamoDBv2" Version="3.*" />
<PackageReference Include="AWSSDK.Extensions.NETCore.Setup" Version="3.*" />
```

---

## 3. Table Design

### 3.1 Events Table

| Attribute | Type | Key | Description |
|-----------|------|-----|-------------|
| `pk` | String | Partition Key | `EVENT#{eventId}` |
| `sk` | String | Sort Key | `EVENT#{eventId}` |
| `eventId` | String | - | UUID string |
| `sortableUniqueId` | String | GSI1-SK | Sortable timestamp-based ID |
| `eventType` | String | - | Event type name |
| `payload` | String | - | JSON serialized payload |
| `tags` | List<String> | - | Tag list |
| `timestamp` | String | - | ISO8601 UTC timestamp |
| `causationId` | String | - | Command ID (optional) |
| `correlationId` | String | - | Correlation ID (optional) |
| `executedUser` | String | - | Executing user (optional) |
| `ttl` | Number | - | TTL epoch (optional) |

**Global Secondary Index 1 (GSI1)** - For chronological event queries:
| Attribute | Type | Key |
|-----------|------|-----|
| `gsi1pk` | String | Partition Key | Fixed value `ALL_EVENTS` |
| `sortableUniqueId` | String | Sort Key | Sortable unique ID |

### 3.2 Tags Table

| Attribute | Type | Key | Description |
|-----------|------|-----|-------------|
| `pk` | String | Partition Key | `TAG#{tagString}` |
| `sk` | String | Sort Key | `{sortableUniqueId}#{eventId}` |
| `tagString` | String | - | Full tag string |
| `tagGroup` | String | GSI1-PK | Tag group/category |
| `eventType` | String | - | Event type |
| `eventId` | String | - | Reference to event |
| `sortableUniqueId` | String | - | For ordering |
| `createdAt` | String | - | ISO8601 creation time |

**Global Secondary Index 1 (GSI1)** - For listing tags by group:
| Attribute | Type | Key |
|-----------|------|-----|
| `tagGroup` | String | Partition Key |
| `tagString` | String | Sort Key |

### 3.3 MultiProjectionStates Table

| Attribute | Type | Key | Description |
|-----------|------|-----|-------------|
| `pk` | String | Partition Key | `PROJECTOR#{projectorName}` |
| `sk` | String | Sort Key | `VERSION#{projectorVersion}` |
| `projectorName` | String | - | Projector identifier |
| `projectorVersion` | String | - | Version string |
| `lastSortableUniqueId` | String | - | Last processed event ID |
| `eventsProcessed` | Number | - | Count of processed events |
| `stateData` | String | - | Base64 + Gzip compressed state |
| `isOffloaded` | Boolean | - | S3 offload flag |
| `offloadKey` | String | - | S3 key (if offloaded) |
| `originalSizeBytes` | Number | - | Original state size |
| `compressedSizeBytes` | Number | - | Compressed size |
| `safeWindowThreshold` | String | - | Safe replay threshold |

---

## 4. Key Design Decisions

### 4.1 Partition Key Strategy

#### Events Table
- **Single-item partition**: `pk = EVENT#{eventId}`
- **Rationale**:
  - Enables uniform write distribution across partitions
  - Point reads by eventId are O(1)
  - Avoids hot partition issues during high-throughput writes

#### Tags Table
- **Tag-based partition**: `pk = TAG#{tagString}`
- **Rationale**:
  - All entries for a single tag are co-located
  - Enables efficient tag-based queries
  - Supports TransactWriteItems within same partition

### 4.2 Global Secondary Index Strategy

**GSI1 on Events Table (`ALL_EVENTS` partition)**:
- Single partition key `ALL_EVENTS` with `sortableUniqueId` as sort key
- Enables `ReadAllEventsAsync` with chronological ordering
- **Trade-off**: Hot partition risk for very high write throughput
- **Mitigation**: Use write sharding with multiple partition keys (e.g., `ALL_EVENTS#0` through `ALL_EVENTS#9`) for extreme scale

### 4.3 Cosmos DB vs DynamoDB Feature Mapping

| Cosmos DB | DynamoDB | Notes |
|-----------|----------|-------|
| Container | Table | 1:1 mapping |
| Partition Key `/id` | PK + SK composite | More flexibility |
| TransactionalBatch | TransactWriteItems | 100 item limit both |
| SQL Queries | Query/Scan + GSI | Different query model |
| Change Feed | DynamoDB Streams | Similar functionality |
| Request Units (RU) | Read/Write Capacity Units | Different billing model |
| Direct/Gateway mode | N/A | DynamoDB handles routing |

---

## 5. Implementation Details

### 5.1 IEventStore Implementation

```csharp
public class DynamoDbEventStore : IEventStore
{
    private readonly DynamoDbContext _context;
    private readonly DcbDomainTypes _domainTypes;
    private readonly ILogger? _logger;

    // Read operations
    public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null);
    public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
    public Task<ResultBox<Event>> ReadEventAsync(Guid eventId);

    // Write operations
    public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events);

    // Tag operations
    public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag);
    public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag);
    public Task<ResultBox<bool>> TagExistsAsync(ITag tag);
    public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null);
    public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null);
}
```

### 5.2 Write Operation Flow

```
WriteEventsAsync:
  ┌─ Step 1: Convert events to DynamoDB items
  │
  ├─ Step 2: Group by transaction boundaries (max 100 items)
  │
  ├─ Step 3: Execute TransactWriteItems for each group
  │    - Events written to Events table
  │    - Tags written to Tags table (same transaction)
  │    - Use ClientRequestToken for idempotency
  │
  └─ Step 4: Return results with write statistics
```

**Transaction Strategy**:
```csharp
var transactItems = new List<TransactWriteItem>();

// Add event items
foreach (var evt in eventsBatch)
{
    transactItems.Add(new TransactWriteItem
    {
        Put = new Put
        {
            TableName = _eventsTableName,
            Item = ToDynamoItem(evt),
            ConditionExpression = "attribute_not_exists(pk)" // Prevent duplicates
        }
    });
}

// Add tag items (same transaction)
foreach (var tag in tagsBatch)
{
    transactItems.Add(new TransactWriteItem
    {
        Put = new Put
        {
            TableName = _tagsTableName,
            Item = ToDynamoItem(tag)
        }
    });
}

await _client.TransactWriteItemsAsync(new TransactWriteItemsRequest
{
    TransactItems = transactItems,
    ClientRequestToken = idempotencyToken // 10-minute idempotency window
});
```

### 5.3 Read Operation Patterns

#### ReadAllEventsAsync
```csharp
// Query GSI1 for chronological order
var request = new QueryRequest
{
    TableName = _eventsTableName,
    IndexName = "GSI1",
    KeyConditionExpression = "gsi1pk = :pk AND sortableUniqueId > :since",
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":pk"] = new AttributeValue { S = "ALL_EVENTS" },
        [":since"] = new AttributeValue { S = since?.Value ?? "" }
    },
    ScanIndexForward = true // Ascending order
};
```

#### ReadEventsByTagAsync
```csharp
// Step 1: Query Tags table for event IDs
var tagQuery = new QueryRequest
{
    TableName = _tagsTableName,
    KeyConditionExpression = "pk = :pk AND sk > :since",
    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
    {
        [":pk"] = new AttributeValue { S = $"TAG#{tagString}" },
        [":since"] = new AttributeValue { S = since?.Value ?? "" }
    }
};

// Step 2: BatchGetItem for event details
var batchGetRequest = new BatchGetItemRequest
{
    RequestItems = new Dictionary<string, KeysAndAttributes>
    {
        [_eventsTableName] = new KeysAndAttributes
        {
            Keys = eventIds.Select(id => new Dictionary<string, AttributeValue>
            {
                ["pk"] = new AttributeValue { S = $"EVENT#{id}" },
                ["sk"] = new AttributeValue { S = $"EVENT#{id}" }
            }).ToList()
        }
    }
};
```

### 5.4 Configuration Options

```csharp
public class DynamoDbEventStoreOptions
{
    // Table names
    public string EventsTableName { get; set; } = "SekibanEvents";
    public string TagsTableName { get; set; } = "SekibanTags";
    public string ProjectionStatesTableName { get; set; } = "SekibanProjectionStates";

    // Performance tuning
    public int MaxConcurrentReads { get; set; } = Environment.ProcessorCount * 2;
    public int MaxBatchSize { get; set; } = 25; // BatchGetItem limit
    public int MaxTransactionItems { get; set; } = 100; // TransactWriteItems limit
    public bool UseConsistentReads { get; set; } = false;

    // Retry configuration
    public int MaxRetryAttempts { get; set; } = 10;
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    // Write sharding (for high throughput)
    public int WriteShardCount { get; set; } = 1; // Set > 1 for write sharding

    // S3 offloading for large projection states
    public string? S3BucketName { get; set; }
    public long OffloadThresholdBytes { get; set; } = 400_000; // 400KB DynamoDB item limit
}
```

---

## 6. DI Registration

### 6.1 Standard Registration

```csharp
public static class SekibanDcbDynamoDbExtensions
{
    public static IServiceCollection AddSekibanDcbDynamoDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DynamoDbEventStoreOptions>(
            configuration.GetSection("DynamoDb"));

        services.AddAWSService<IAmazonDynamoDB>();
        services.AddSingleton<DynamoDbContext>();
        services.AddSingleton<IEventStore, DynamoDbEventStore>();
        services.AddSingleton<IMultiProjectionStateStore, DynamoMultiProjectionStateStore>();
        services.AddHostedService<DynamoDbInitializer>();

        return services;
    }

    public static IServiceCollection AddSekibanDcbDynamoDb(
        this IServiceCollection services,
        string region,
        DynamoDbEventStoreOptions? options = null)
    {
        // Explicit configuration
    }
}
```

### 6.2 Aspire Integration

```csharp
public static IServiceCollection AddSekibanDcbDynamoDbWithAspire(
    this IServiceCollection services)
{
    // Look for Aspire-provided DynamoDB client
    // Fall back to configuration-based setup
}
```

### 6.3 Configuration Example

```json
{
  "AWS": {
    "Region": "ap-northeast-1"
  },
  "DynamoDb": {
    "EventsTableName": "SekibanEvents",
    "TagsTableName": "SekibanTags",
    "ProjectionStatesTableName": "SekibanProjectionStates",
    "MaxConcurrentReads": 16,
    "UseConsistentReads": false
  },
  "S3BlobStorage": {
    "BucketName": "my-sekiban-snapshots",
    "Prefix": "projections",
    "EnableEncryption": true
  }
}
```

### 6.4 Full Registration Example

```csharp
// Program.cs
services.AddSekibanDcbDynamoDb(configuration);
services.AddSekibanDcbS3BlobStorage(configuration); // Required for projection state offloading
```

**Important**: The S3 package is required because DynamoDB has a 400KB item size limit. Projection states can easily exceed this limit, requiring offloading to S3.

---

## 7. Consistency and Atomicity

### 7.1 Write Atomicity

DynamoDB TransactWriteItems provides:
- **All-or-nothing**: Either all items succeed or all fail
- **Isolation**: Serializable isolation within transaction
- **100 item limit**: Must batch larger writes
- **4MB total size limit**: Must handle large payloads

**Implementation Strategy**:
```
For N events with M total tags:
  If (N + M) <= 100:
    Single TransactWriteItems
  Else:
    Split into multiple transactions
    Use idempotency tokens for retry safety
    Implement rollback on partial failure
```

### 7.2 Read Consistency

| Operation | Consistency | Rationale |
|-----------|-------------|-----------|
| ReadEventAsync (point read) | Eventually/Strong | Configurable |
| ReadAllEventsAsync (GSI query) | Eventually | GSI is always eventually consistent |
| ReadEventsByTagAsync | Eventually | Tags table + BatchGetItem |
| GetLatestTagAsync | Eventually/Strong | Configurable |

**Note**: GSI queries are always eventually consistent in DynamoDB. For strong consistency requirements, consider direct table queries with careful key design.

### 7.3 Idempotency

DynamoDB's `ClientRequestToken` provides native idempotency for transactions:

```csharp
/// <summary>
/// Generate deterministic idempotency token from event IDs.
/// Token must be 1-36 characters, valid for 10 minutes after initial request.
/// </summary>
private static string GenerateIdempotencyToken(IEnumerable<Event> events)
{
    // Create deterministic hash from event IDs
    var eventIds = string.Join(",", events.Select(e => e.Id.ToString()).OrderBy(id => id));
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(eventIds));
    // Take first 36 characters (DynamoDB limit)
    return Convert.ToBase64String(hashBytes).Substring(0, 36);
}

var request = new TransactWriteItemsRequest
{
    TransactItems = items,
    ClientRequestToken = GenerateIdempotencyToken(events)
};
```

**Idempotency Behavior**:
- If initial call succeeds, subsequent calls with same token return success without changes
- Token valid for 10 minutes after initial request completes
- Different request parameters with same token cause `IdempotentParameterMismatch` exception
- Enables safe retries without duplicate writes

---

## 8. Error Handling

### 8.1 Retry Strategy

```csharp
// Using AWS SDK built-in retry with custom configuration
var config = new AmazonDynamoDBConfig
{
    RegionEndpoint = RegionEndpoint.APNortheast1,
    MaxErrorRetry = 10,
    Timeout = TimeSpan.FromSeconds(30)
};
```

### 8.2 Exception Handling

| Exception | Handling |
|-----------|----------|
| `ProvisionedThroughputExceededException` | Exponential backoff retry |
| `TransactionCanceledException` | Check reasons, retry or fail |
| `ConditionalCheckFailedException` | Duplicate event, skip |
| `ValidationException` | Log and throw |
| `ResourceNotFoundException` | Initialize tables |

### 8.3 Rollback Strategy

```csharp
// On partial transaction failure
private async Task TryRollbackEventsAsync(
    IEnumerable<Event> writtenEvents,
    CancellationToken ct)
{
    var deleteRequests = writtenEvents.Select(e => new WriteRequest
    {
        DeleteRequest = new DeleteRequest
        {
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new AttributeValue { S = $"EVENT#{e.Id}" },
                ["sk"] = new AttributeValue { S = $"EVENT#{e.Id}" }
            }
        }
    }).ToList();

    // BatchWriteItem for deletion
    await _client.BatchWriteItemAsync(new BatchWriteItemRequest
    {
        RequestItems = new Dictionary<string, List<WriteRequest>>
        {
            [_eventsTableName] = deleteRequests
        }
    }, ct);
}
```

---

## 9. Performance Considerations

### 9.1 Write Optimization

| Technique | Description |
|-----------|-------------|
| TransactWriteItems | Atomic multi-item writes |
| BatchWriteItem | Non-transactional bulk writes (faster) |
| Parallel writes | For independent events |
| Write sharding | Distribute GSI partition key load |

### 9.2 Read Optimization

| Technique | Description |
|-----------|-------------|
| BatchGetItem | Up to 100 items, 16MB limit |
| Parallel queries | Multiple partition queries |
| Projection expressions | Return only needed attributes |
| Consistent reads | Only when necessary |

### 9.3 Cost Optimization

| Strategy | Impact |
|----------|--------|
| On-demand capacity | Best for variable workloads |
| Provisioned capacity | Best for predictable workloads |
| Reserved capacity | Up to 77% savings for committed throughput |
| TTL | Automatic deletion of old items |
| Sparse GSI | Only index items with specific attributes |

---

## 10. Testing Strategy

### 10.1 Local Development

```csharp
// Use DynamoDB Local for development/testing
services.AddSingleton<IAmazonDynamoDB>(sp =>
{
    var config = new AmazonDynamoDBConfig
    {
        ServiceURL = "http://localhost:8000"
    };
    return new AmazonDynamoDBClient(config);
});
```

### 10.2 Integration Tests

```csharp
public class DynamoDbEventStoreTests : IAsyncLifetime
{
    private readonly DynamoDbEventStore _eventStore;

    public async Task InitializeAsync()
    {
        // Create test tables with unique names
        await CreateTestTables();
    }

    public async Task DisposeAsync()
    {
        // Clean up test tables
        await DeleteTestTables();
    }

    [Fact]
    public async Task WriteAndReadEvents_ShouldMaintainOrder()
    {
        // Test implementation
    }
}
```

---

## 11. Migration Path

### 11.1 From Cosmos DB

1. Export events from Cosmos DB
2. Transform to DynamoDB format
3. Bulk import using BatchWriteItem
4. Verify data integrity
5. Switch configuration

### 11.2 Backward Compatibility

- Same `IEventStore` interface
- Same event format (JSON payload)
- Same tag semantics
- Configuration-based provider selection

---

## 12. Sample Project

### 12.1 Project Structure

```
internalUsages/
├── DcbOrleans.AppHost/              # Existing (Postgres)
├── DcbOrleansDynamoDB.AppHost/      # NEW - DynamoDB version
│   ├── Program.cs
│   ├── appsettings.json
│   └── docker-compose.localstack.yml
├── DcbOrleansDynamoDB.Api/          # NEW - API with DynamoDB
│   ├── Program.cs
│   └── appsettings.json
└── DcbOrleansDynamoDB.Web/          # Optional - share existing or create new
```

### 12.2 AppHost Configuration

```csharp
// DcbOrleansDynamoDB.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// LocalStack for DynamoDB + S3
var localstack = builder.AddContainer("localstack", "localstack/localstack")
    .WithEndpoint(port: 4566, targetPort: 4566, name: "gateway")
    .WithEnvironment("SERVICES", "dynamodb,s3")
    .WithEnvironment("PERSISTENCE", "1");

var api = builder.AddProject<Projects.DcbOrleansDynamoDB_Api>("api")
    .WithReference(localstack)
    .WaitFor(localstack);

builder.Build().Run();
```

### 12.3 API Configuration

```csharp
// DcbOrleansDynamoDB.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Sekiban DCB with DynamoDB
builder.Services.AddSekibanDcbDynamoDb(builder.Configuration);
builder.Services.AddSekibanDcbS3BlobStorage(builder.Configuration);

// Orleans (optional)
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    // DynamoDB grain storage configuration if needed
});
```

---

## 13. Future Enhancements

### 12.1 DynamoDB Streams Integration

```csharp
// Real-time projection updates via DynamoDB Streams
public class DynamoDbStreamProcessor
{
    public async Task ProcessRecordAsync(StreamRecord record)
    {
        // Update projections in real-time
    }
}
```

### 12.2 Global Tables

- Multi-region active-active replication
- Automatic conflict resolution
- Cross-region disaster recovery

### 12.3 Point-in-Time Recovery

- Automatic continuous backups
- Restore to any second in the last 35 days

---

## 13. References

- [AWS DynamoDB Developer Guide](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/)
- [DynamoDB Transactions](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/transaction-apis.html)
- [TransactWriteItems API](https://docs.aws.amazon.com/amazondynamodb/latest/APIReference/API_TransactWriteItems.html)
- [GSI Best Practices](https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/bp-indexes.html)
- [.NET SDK for DynamoDB](https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/DynamoDBv2/MDynamoDBTransactWriteItemsTransactWriteItemsRequest.html)
- [DynamoDB Transactions in .NET (Rahul Nath)](https://www.rahulpnath.com/blog/amazon-dynamodb-transactions-dotnet)

---

## 14. Appendix

### A. Complete Table Creation Script

```csharp
public async Task CreateTablesAsync()
{
    // Events table
    await _client.CreateTableAsync(new CreateTableRequest
    {
        TableName = "SekibanEvents",
        KeySchema = new List<KeySchemaElement>
        {
            new KeySchemaElement("pk", KeyType.HASH),
            new KeySchemaElement("sk", KeyType.RANGE)
        },
        AttributeDefinitions = new List<AttributeDefinition>
        {
            new AttributeDefinition("pk", ScalarAttributeType.S),
            new AttributeDefinition("sk", ScalarAttributeType.S),
            new AttributeDefinition("gsi1pk", ScalarAttributeType.S),
            new AttributeDefinition("sortableUniqueId", ScalarAttributeType.S)
        },
        GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
        {
            new GlobalSecondaryIndex
            {
                IndexName = "GSI1",
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement("gsi1pk", KeyType.HASH),
                    new KeySchemaElement("sortableUniqueId", KeyType.RANGE)
                },
                Projection = new Projection { ProjectionType = ProjectionType.ALL }
            }
        },
        BillingMode = BillingMode.PAY_PER_REQUEST
    });

    // Tags table
    await _client.CreateTableAsync(new CreateTableRequest
    {
        TableName = "SekibanTags",
        KeySchema = new List<KeySchemaElement>
        {
            new KeySchemaElement("pk", KeyType.HASH),
            new KeySchemaElement("sk", KeyType.RANGE)
        },
        AttributeDefinitions = new List<AttributeDefinition>
        {
            new AttributeDefinition("pk", ScalarAttributeType.S),
            new AttributeDefinition("sk", ScalarAttributeType.S),
            new AttributeDefinition("tagGroup", ScalarAttributeType.S),
            new AttributeDefinition("tagString", ScalarAttributeType.S)
        },
        GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
        {
            new GlobalSecondaryIndex
            {
                IndexName = "GSI1",
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement("tagGroup", KeyType.HASH),
                    new KeySchemaElement("tagString", KeyType.RANGE)
                },
                Projection = new Projection { ProjectionType = ProjectionType.KEYS_ONLY }
            }
        },
        BillingMode = BillingMode.PAY_PER_REQUEST
    });

    // ProjectionStates table
    await _client.CreateTableAsync(new CreateTableRequest
    {
        TableName = "SekibanProjectionStates",
        KeySchema = new List<KeySchemaElement>
        {
            new KeySchemaElement("pk", KeyType.HASH),
            new KeySchemaElement("sk", KeyType.RANGE)
        },
        AttributeDefinitions = new List<AttributeDefinition>
        {
            new AttributeDefinition("pk", ScalarAttributeType.S),
            new AttributeDefinition("sk", ScalarAttributeType.S)
        },
        BillingMode = BillingMode.PAY_PER_REQUEST
    });
}
```

### B. Environment-Specific Configuration

```json
// appsettings.Development.json (DynamoDB Local)
{
  "DynamoDb": {
    "ServiceURL": "http://localhost:8000",
    "UseConsistentReads": true
  }
}

// appsettings.Production.json (AWS)
{
  "AWS": {
    "Region": "ap-northeast-1"
  },
  "DynamoDb": {
    "EventsTableName": "prod-SekibanEvents",
    "TagsTableName": "prod-SekibanTags",
    "MaxConcurrentReads": 32,
    "WriteShardCount": 4
  }
}
```

### C. AWS Credentials and IAM Configuration

#### C.1 Credential Resolution Order

The AWS SDK uses the following credential resolution order:

1. **Environment variables**: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`
2. **Shared credentials file**: `~/.aws/credentials`
3. **AWS config file**: `~/.aws/config`
4. **ECS container credentials**: Via `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI`
5. **EC2 instance profile**: IAM role attached to instance
6. **EKS Pod Identity**: For Kubernetes workloads

#### C.2 Required IAM Permissions

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "SekibanDynamoDBAccess",
      "Effect": "Allow",
      "Action": [
        "dynamodb:CreateTable",
        "dynamodb:DescribeTable",
        "dynamodb:GetItem",
        "dynamodb:PutItem",
        "dynamodb:DeleteItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query",
        "dynamodb:Scan",
        "dynamodb:BatchGetItem",
        "dynamodb:BatchWriteItem",
        "dynamodb:TransactWriteItems",
        "dynamodb:TransactGetItems"
      ],
      "Resource": [
        "arn:aws:dynamodb:*:*:table/SekibanEvents",
        "arn:aws:dynamodb:*:*:table/SekibanEvents/index/*",
        "arn:aws:dynamodb:*:*:table/SekibanTags",
        "arn:aws:dynamodb:*:*:table/SekibanTags/index/*",
        "arn:aws:dynamodb:*:*:table/SekibanProjectionStates"
      ]
    },
    {
      "Sid": "SekibanS3Access",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::sekiban-snapshots",
        "arn:aws:s3:::sekiban-snapshots/*"
      ]
    }
  ]
}
```

#### C.3 Configuration in appsettings.json

```json
{
  "AWS": {
    "Profile": "sekiban-dev",
    "Region": "ap-northeast-1"
  }
}
```

Or for explicit credentials (not recommended for production):
```json
{
  "AWS": {
    "Region": "ap-northeast-1",
    "AccessKey": "AKIA...",
    "SecretKey": "..."
  }
}
```

### D. LocalStack Development Setup

#### D.1 Docker Compose Configuration

```yaml
# docker-compose.localstack.yml
version: '3.8'
services:
  localstack:
    image: localstack/localstack:latest
    ports:
      - "4566:4566"           # LocalStack Gateway
      - "4510-4559:4510-4559" # External services port range
    environment:
      - SERVICES=dynamodb,s3
      - DEBUG=1
      - PERSISTENCE=1
      - DYNAMODB_SHARE_DB=1
    volumes:
      - "./localstack-data:/var/lib/localstack"
      - "/var/run/docker.sock:/var/run/docker.sock"
```

#### D.2 LocalStack Configuration

```json
// appsettings.LocalStack.json
{
  "DynamoDb": {
    "ServiceURL": "http://localhost:4566",
    "ForcePathStyle": true,
    "EventsTableName": "SekibanEvents",
    "TagsTableName": "SekibanTags",
    "ProjectionStatesTableName": "SekibanProjectionStates"
  },
  "S3BlobStorage": {
    "ServiceUrl": "http://localhost:4566",
    "ForcePathStyle": true,
    "BucketName": "sekiban-snapshots"
  },
  "AWS": {
    "AccessKey": "test",
    "SecretKey": "test",
    "Region": "us-east-1"
  }
}
```

#### D.3 LocalStack Initialization Script

```bash
#!/bin/bash
# init-localstack.sh

# Create DynamoDB tables
awslocal dynamodb create-table \
  --table-name SekibanEvents \
  --key-schema AttributeName=pk,KeyType=HASH AttributeName=sk,KeyType=RANGE \
  --attribute-definitions \
    AttributeName=pk,AttributeType=S \
    AttributeName=sk,AttributeType=S \
    AttributeName=gsi1pk,AttributeType=S \
    AttributeName=sortableUniqueId,AttributeType=S \
  --global-secondary-indexes \
    'IndexName=GSI1,KeySchema=[{AttributeName=gsi1pk,KeyType=HASH},{AttributeName=sortableUniqueId,KeyType=RANGE}],Projection={ProjectionType=ALL}' \
  --billing-mode PAY_PER_REQUEST

awslocal dynamodb create-table \
  --table-name SekibanTags \
  --key-schema AttributeName=pk,KeyType=HASH AttributeName=sk,KeyType=RANGE \
  --attribute-definitions \
    AttributeName=pk,AttributeType=S \
    AttributeName=sk,AttributeType=S \
    AttributeName=tagGroup,AttributeType=S \
    AttributeName=tagString,AttributeType=S \
  --global-secondary-indexes \
    'IndexName=GSI1,KeySchema=[{AttributeName=tagGroup,KeyType=HASH},{AttributeName=tagString,KeyType=RANGE}],Projection={ProjectionType=KEYS_ONLY}' \
  --billing-mode PAY_PER_REQUEST

awslocal dynamodb create-table \
  --table-name SekibanProjectionStates \
  --key-schema AttributeName=pk,KeyType=HASH AttributeName=sk,KeyType=RANGE \
  --attribute-definitions \
    AttributeName=pk,AttributeType=S \
    AttributeName=sk,AttributeType=S \
  --billing-mode PAY_PER_REQUEST

# Create S3 bucket
awslocal s3 mb s3://sekiban-snapshots

echo "LocalStack initialized successfully"
```

#### D.4 DynamoDB Local Alternative

For simpler local testing without S3:

```bash
# Using DynamoDB Local
docker run -p 8000:8000 amazon/dynamodb-local

# Configuration
{
  "DynamoDb": {
    "ServiceURL": "http://localhost:8000"
  }
}
```

### E. Table Creation Strategy

#### E.1 Automatic (Application-Managed)

Tables created automatically by `DynamoDbInitializer` on startup:

```csharp
public class DynamoDbInitializer : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await CreateTableIfNotExistsAsync(_options.EventsTableName, EventsTableSchema);
        await CreateTableIfNotExistsAsync(_options.TagsTableName, TagsTableSchema);
        await CreateTableIfNotExistsAsync(_options.ProjectionStatesTableName, ProjectionStatesTableSchema);
    }
}
```

**Pros**: Simple setup, good for development
**Cons**: Requires CreateTable permission, not suitable for all production scenarios

#### E.2 Infrastructure-Managed (Recommended for Production)

Tables created via IaC (Terraform, CloudFormation, CDK):

```hcl
# terraform/dynamodb.tf
resource "aws_dynamodb_table" "sekiban_events" {
  name         = "SekibanEvents"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "pk"
  range_key    = "sk"

  attribute {
    name = "pk"
    type = "S"
  }
  attribute {
    name = "sk"
    type = "S"
  }
  attribute {
    name = "gsi1pk"
    type = "S"
  }
  attribute {
    name = "sortableUniqueId"
    type = "S"
  }

  global_secondary_index {
    name            = "GSI1"
    hash_key        = "gsi1pk"
    range_key       = "sortableUniqueId"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = true
  }

  tags = {
    Application = "Sekiban"
    Environment = "Production"
  }
}
```

**Configuration for infrastructure-managed**:
```csharp
services.AddSekibanDcbDynamoDb(configuration, new DynamoDbEventStoreOptions
{
    AutoCreateTables = false // Skip table creation
});
```

### F. Write Sharding Implementation Detail

For high-throughput scenarios, GSI hot partition can be mitigated:

```csharp
public class DynamoDbEventStoreOptions
{
    /// <summary>
    /// Number of shards for GSI partition key.
    /// When > 1, gsi1pk becomes "ALL_EVENTS#{hash % WriteShardCount}"
    /// </summary>
    public int WriteShardCount { get; set; } = 1;
}

// Write: distribute across shards
private string GetGsi1PartitionKey(string sortableUniqueId)
{
    if (_options.WriteShardCount <= 1)
        return "ALL_EVENTS";

    var hash = Math.Abs(sortableUniqueId.GetHashCode());
    var shard = hash % _options.WriteShardCount;
    return $"ALL_EVENTS#{shard}";
}

// Read: scatter-gather across all shards
public async Task<IEnumerable<Event>> ReadAllEventsAsync(SortableUniqueId? since)
{
    if (_options.WriteShardCount <= 1)
    {
        return await QuerySingleShardAsync("ALL_EVENTS", since);
    }

    // Query all shards in parallel
    var tasks = Enumerable.Range(0, _options.WriteShardCount)
        .Select(shard => QuerySingleShardAsync($"ALL_EVENTS#{shard}", since));

    var results = await Task.WhenAll(tasks);

    // Merge and sort by sortableUniqueId
    return results
        .SelectMany(r => r)
        .OrderBy(e => e.SortableUniqueId);
}
```
