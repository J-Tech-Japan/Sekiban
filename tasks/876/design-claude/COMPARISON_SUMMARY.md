# Design Comparison Summary

## Quick Reference for LLM Comparison Review

This document summarizes key design decisions for easier comparison with other LLM designs.

---

## 1. Table Design Summary

### Option Chosen: 3-Table Design

| Table | Partition Key | Sort Key | GSI |
|-------|---------------|----------|-----|
| Events | `EVENT#{eventId}` | `EVENT#{eventId}` | GSI1: `ALL_EVENTS` + `sortableUniqueId` |
| Tags | `TAG#{tagString}` | `{sortableUniqueId}#{eventId}` | GSI1: `tagGroup` + `tagString` |
| ProjectionStates | `PROJECTOR#{name}` | `VERSION#{version}` | None |

### Alternative Considered: Single-Table Design

Could use single table with item type prefix, but chose separate tables for:
- Clearer capacity management
- Simpler GSI design
- Easier monitoring and troubleshooting

---

## 2. Key Design Decisions

### 2.1 Event Storage

| Decision | Choice | Rationale |
|----------|--------|-----------|
| PK pattern | `EVENT#{id}` | Single-item partition for write distribution |
| SK pattern | Same as PK | Simple point read, no range queries needed |
| GSI for ordering | `ALL_EVENTS` + `sortableUniqueId` | Required for `ReadAllEventsAsync` |

### 2.2 Tag Storage

| Decision | Choice | Rationale |
|----------|--------|-----------|
| PK pattern | `TAG#{tagString}` | Co-locate all entries for same tag |
| SK pattern | `{sortableUniqueId}#{eventId}` | Ordered by time, unique |
| GSI | `tagGroup` + `tagString` | For `GetAllTagsAsync` |

### 2.3 Write Strategy

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Transaction API | `TransactWriteItems` | Atomic writes for events + tags |
| Batch limit | 100 items | DynamoDB transaction limit |
| Idempotency | `ClientRequestToken` | Built-in 10-minute idempotency |

---

## 3. Trade-offs Analysis

### 3.1 GSI `ALL_EVENTS` Partition

**Pro**: Simple implementation for chronological queries
**Con**: Potential hot partition for high write throughput

**Mitigation**: Write sharding option (`ALL_EVENTS#0` through `ALL_EVENTS#N`)

### 3.2 Eventual Consistency on GSI

**Pro**: Better performance, lower cost
**Con**: Not suitable for all use cases

**Mitigation**: Direct table queries for strong consistency (where possible)

### 3.3 Transaction Size Limit (100 items)

**Pro**: Atomic writes within limit
**Con**: Multi-transaction writes lose atomicity

**Mitigation**:
- Idempotency tokens for retry safety
- Rollback mechanism for partial failures

---

## 4. Cosmos DB Mapping

| Cosmos DB | DynamoDB (This Design) |
|-----------|------------------------|
| `/id` partition key | `EVENT#{id}` PK |
| TransactionalBatch (100 items) | TransactWriteItems (100 items) |
| SQL queries | Query/Scan with GSI |
| Request Units | Read/Write Capacity Units |
| Change Feed | DynamoDB Streams |
| Strong/Session consistency | Eventually consistent (GSI) |

---

## 5. Open Questions for Review

1. ~~**Single-table vs Multi-table**: Is 3-table design optimal?~~ → **Resolved**: Multi-table for Cosmos DB parity
2. ~~**GSI hot partition**: Better alternatives to `ALL_EVENTS` partition?~~ → **Resolved**: Write sharding option added
3. **Consistency trade-offs**: Acceptable for event sourcing use cases? (GSI is eventually consistent)
4. ~~**Large state offloading**: S3 integration design sufficient?~~ → **Resolved**: S3 package confirmed required
5. ~~**Write sharding complexity**: Worth the added complexity?~~ → **Resolved**: Optional, configurable

### Resolved via Codex Review
- Tag SK collision prevention: `{sortableUniqueId}#{eventId}` composite
- Idempotency token generation: Deterministic hash from event IDs
- Transaction vs Batch limits: TransactWriteItems=100, BatchWriteItem=25
- IAM/AWS credentials documentation: Added to appendix
- LocalStack setup: Added detailed configuration

---

## 6. Cost Estimation (Reference)

### On-Demand Pricing (Tokyo Region)

| Operation | Cost |
|-----------|------|
| Write (WCU) | $1.4846 per million |
| Read (RCU) | $0.2969 per million |
| Storage | $0.285 per GB-month |
| GSI Write | Same as base table |
| GSI Read | Same as base table |

### Sample Workload (1M events/day)

| Component | Monthly Cost (approx) |
|-----------|----------------------|
| Event writes | ~$50 |
| Tag writes | ~$50 |
| GSI updates | ~$100 |
| Reads | ~$30 |
| Storage (10GB) | ~$3 |
| **Total** | **~$233** |

---

## 7. Implementation Complexity

| Component | Complexity | Notes |
|-----------|------------|-------|
| Basic CRUD | Low | Standard SDK patterns |
| Transactions | Medium | Batching logic needed |
| GSI queries | Low | Simple query patterns |
| Error handling | Medium | Retry/rollback logic |
| Testing (local) | Low | DynamoDB Local available |
| Production setup | Low | On-demand = minimal config |

---

## 8. Summary

This design prioritizes:
1. **API compatibility** with Cosmos DB implementation
2. **Write performance** through distributed partitions
3. **Simplicity** over advanced optimization
4. **Extensibility** for future enhancements (streams, global tables)

Main trade-off: GSI eventual consistency for easier implementation.
