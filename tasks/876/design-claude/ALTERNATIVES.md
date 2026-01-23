# Alternative Designs Considered

This document explores alternative approaches that were considered but not chosen as the primary design.

---

## Alternative 1: Single-Table Design

### Description

Use a single DynamoDB table for all data (events, tags, projection states) with different prefixes.

```
| pk                     | sk                          | Type   |
|------------------------|-----------------------------|--------|
| EVENT#{eventId}        | EVENT#{eventId}             | Event  |
| TAG#{tagString}        | {sortableUniqueId}#{eventId}| Tag    |
| PROJECTOR#{name}       | VERSION#{version}           | State  |
| ALL_EVENTS             | {sortableUniqueId}          | Index  |
```

### Pros
- Single table = simpler capacity management
- Follows DynamoDB single-table design best practices
- Can use transactions across all item types easily

### Cons
- More complex queries (need to filter by item type)
- GSI design becomes more complex
- Harder to monitor/debug specific data types
- Different access patterns may conflict

### Why Not Chosen
The Cosmos DB implementation uses separate containers, and maintaining similar structure makes the codebase more consistent and easier to maintain.

---

## Alternative 2: Event-Centric Design with Embedded Tags

### Description

Store tags directly in the event item instead of a separate table.

```
Events Table:
| pk              | sk              | tags (List)      |
|-----------------|-----------------|------------------|
| EVENT#{eventId} | EVENT#{eventId} | ["Tag1", "Tag2"] |

GSI for tag queries:
| gsi_pk          | gsi_sk          |
|-----------------|-----------------|
| TAG#{tagString} | {sortableUniqueId} |
```

### Pros
- Fewer tables to manage
- Atomic tag storage with events
- No consistency delay between event and tag

### Cons
- DynamoDB GSI doesn't support indexing into List attributes
- Would need to duplicate data or create complex GSI patterns
- Tag updates would require event updates

### Why Not Chosen
DynamoDB cannot efficiently index List attributes, making tag queries impractical without a separate structure.

---

## Alternative 3: Time-Based Partitioning for Events

### Description

Partition events by time bucket (e.g., daily or hourly).

```
| pk                    | sk                     |
|-----------------------|------------------------|
| EVENTS#2026-01-22     | {sortableUniqueId}     |
| EVENTS#2026-01-21     | {sortableUniqueId}     |
```

### Pros
- Natural time-range queries
- Better write distribution across partitions
- Easy data lifecycle management (delete old partitions)

### Cons
- Point reads by eventId require knowing the time bucket
- Need additional index for eventId lookups
- Complex query logic for cross-partition reads

### Why Not Chosen
Primary use case requires point reads by eventId, which this design makes inefficient.

---

## Alternative 4: Write-Sharded GSI from Start

### Description

Always use write sharding for the chronological index.

```
| gsi1pk          | gsi1sk              |
|-----------------|---------------------|
| ALL_EVENTS#0    | {sortableUniqueId}  |
| ALL_EVENTS#1    | {sortableUniqueId}  |
| ALL_EVENTS#N    | {sortableUniqueId}  |
```

### Pros
- Better write distribution from day one
- No hot partition issues at scale

### Cons
- More complex read logic (scatter-gather)
- Higher read costs (query all shards)
- Premature optimization for most use cases

### Why Not Chosen
Added complexity not warranted for typical workloads. Made available as an option for high-throughput scenarios.

---

## Alternative 5: DynamoDB Streams for Projection Updates

### Description

Use DynamoDB Streams instead of polling for projection updates.

```
Event Written → DynamoDB Stream → Lambda → Update Projection
```

### Pros
- Real-time projection updates
- No polling overhead
- Event-driven architecture

### Cons
- Additional infrastructure (Lambda)
- Ordering guarantees require careful handling
- More complex deployment and debugging

### Why Not Chosen
Marked as future enhancement. Initial implementation follows the polling pattern established in Cosmos DB for consistency.

---

## Alternative 6: Separate Tables per Tag Group

### Description

Create separate tables for different tag groups.

```
Tags_Accounts Table: TAG#Account:* entries
Tags_Orders Table: TAG#Order:* entries
```

### Pros
- Better isolation between domains
- Independent scaling per domain
- Simpler capacity planning

### Cons
- Table proliferation
- Complex configuration management
- Harder to query across domains

### Why Not Chosen
Single tags table with GSI provides sufficient flexibility without infrastructure complexity.

---

## Alternative 7: Strong Consistency via Conditional Writes

### Description

Use conditional expressions to enforce ordering constraints.

```csharp
Put = new Put
{
    ConditionExpression = "attribute_not_exists(pk) OR sortableUniqueId < :newId",
    ExpressionAttributeValues = { [":newId"] = newSortableId }
}
```

### Pros
- Stronger consistency guarantees
- Prevents out-of-order writes

### Cons
- Higher latency
- More transaction failures
- Complex retry logic

### Why Not Chosen
SortableUniqueId already provides sufficient ordering guarantees at the application level.

---

## Decision Matrix

| Alternative | Complexity | Performance | Consistency | Chosen |
|-------------|------------|-------------|-------------|--------|
| Multi-table (chosen) | Low | Good | Eventual | ✅ |
| Single-table | Medium | Good | Eventual | ❌ |
| Embedded tags | Low | Poor | Strong | ❌ |
| Time-based partitions | High | Excellent | Eventual | ❌ |
| Write sharding always | Medium | Excellent | Eventual | ❌ (option) |
| DynamoDB Streams | High | Excellent | Eventual | ❌ (future) |
| Per-tag-group tables | High | Good | Eventual | ❌ |
| Strong consistency writes | Medium | Poor | Strong | ❌ |

---

## Conclusion

The chosen multi-table design provides the best balance of:
- **Simplicity**: Easy to understand and maintain
- **Performance**: Good enough for typical workloads
- **Flexibility**: Options for scaling when needed
- **Consistency**: Matches Cosmos DB implementation patterns

Advanced features (write sharding, DynamoDB Streams) are available as configuration options or future enhancements rather than baseline requirements.
