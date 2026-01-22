# Design Review v1 — Issue 876 (Codex vs Claude)

## 1. Review Scope
This review compares:
- `tasks/876/design-codex/design.md`
- `tasks/876/design-claude/README.md` (+ `ALTERNATIVES.md`, `COMPARISON_SUMMARY.md`, `S3_OFFLOAD_PACKAGE.md`)

Focus: align Codex design with Claude’s more creative/complete architecture while keeping Cosmos DB parity and DCB constraints.

---

## 2. High‑Impact Ideas from Claude (Adopt / Consider)

### 2.1 Explicit DynamoDB Item Size Limit + S3 Offload (Strongly Recommend)
- Claude highlights the **400KB DynamoDB item limit** and proposes a **dedicated S3 offload package** for multi‑projection state.
- Our Codex design mentions optional `IMultiProjectionStateStore`, but does not address the item limit nor offload strategy.
- **Recommendation**: Add a section in Codex design covering:
  - DynamoDB item size limit and risk for projection state.
  - Optional integration with `IBlobStorageSnapshotAccessor` (S3) similar to Cosmos + Blob integration pattern.
  - If implementing `DynamoDbMultiProjectionStateStore`, note that offload is mandatory for large states.

### 2.2 DynamoDB Transaction Idempotency
- Claude uses `ClientRequestToken` for **idempotent TransactWriteItems**, a DynamoDB‑native best practice.
- Codex design mentions rollback but not idempotency tokens.
- **Recommendation**: add deterministic idempotency token generation (from event IDs) to WriteEventsAsync.

### 2.3 Partition Key & Sort Key Prefixing
- Claude uses **prefix‑style keys** (`EVENT#`, `TAG#`, `PROJECTOR#`) which is standard DynamoDB modeling.
- Codex uses direct `id` as PK and `tag` as PK, which is simpler but less explicit and makes it harder to mix item types (if we ever unify or add metadata records).
- **Recommendation**: keep multi‑table (Cosmos‑aligned) but adopt **prefix naming** for clarity and future‑proofing.

### 2.4 Tag Stream Uniqueness (`sortableUniqueId#eventId`)
- Claude’s tag table SK is `{sortableUniqueId}#{eventId}` to guarantee uniqueness and ordering.
- Codex uses `sortableUniqueId` alone and notes potential collisions.
- **Recommendation**: adopt Claude’s SK composition by default.

### 2.5 GSI Hot Partition Mitigation
- Claude calls out hot partition risk for `ALL_EVENTS` GSI and suggests **write sharding** as an optional setting.
- Codex does not mention this trade‑off.
- **Recommendation**: add optional `WriteShardCount` to options and document shard fan‑out queries.

### 2.6 Explicit Retry/Exception Map
- Claude enumerates common DynamoDB exceptions and retry rules.
- Codex only mentions exponential backoff for unprocessed items.
- **Recommendation**: expand the failure handling section to list relevant exceptions and recommended responses.

---

## 3. Gaps in Codex Design (Compared to Claude)

1. **Projection state offload strategy missing** (DynamoDB 400KB limit not addressed).
2. **Idempotency for transactions not specified**.
3. **Write item limit** uses 25 in Codex (from BatchWrite) but **TransactWriteItems supports 100**, and DynamoDB BatchWrite is also 25. Need clarity.
4. **Key naming conventions** not aligned with DynamoDB best practice (prefixes, composite SK).
5. **Alternative designs and trade‑offs** not documented (Claude includes `ALTERNATIVES.md`).
6. **Doc plan** exists but lacks specifics on AWS credentials, IAM roles, LocalStack vs DynamoDB Local, and table creation options.

---

## 4. Adjustments Recommended for Codex Design

### 4.1 Update Event/Tag Table Keys
- Events PK/SK: `EVENT#{eventId}`
- Tags PK: `TAG#{tagString}`
- Tags SK: `{sortableUniqueId}#{eventId}`

This matches Claude and avoids collisions in tags.

### 4.2 Transaction Item Limits
- **TransactWriteItems** limit = **100 items**, not 25.
- **BatchWriteItem** limit = **25 items**.
- Update the WriteEventsAsync strategy to:
  - Use TransactWriteItems if `(events + tags) <= 100`
  - Else chunk into multiple transactions + idempotency tokens

### 4.3 Idempotency Token
- Generate deterministic `ClientRequestToken` from event IDs (hash), enabling safe retries.

### 4.4 S3 Offload Integration (Optional but Realistic)
- Add `Sekiban.Dcb.BlobStorage.S3` package option (per Claude’s S3 design doc).
- In `DynamoDbMultiProjectionStateStore`, offload large state to S3 if threshold exceeded.

### 4.5 GSI Sharding Option
- Add `WriteShardCount` to `DynamoDbEventStoreOptions`.
- When > 1, derive `gsi1pk = ALL_EVENTS#{hash(sortableUniqueId) % shardCount}`.
- ReadAllEventsAsync must scatter‑gather across shards.

### 4.6 Doc Plan Enhancements
Add to doc plan:
- IAM policy + AWS credentials chain
- LocalStack setup examples
- Table creation approach (automatic vs infra managed)
- Example `appsettings.json` with `AWS:Region`, `DynamoDb:*`, `S3BlobStorage:*`

---

## 5. Alignment with Acceptance Criteria

| Criterion | Codex (Current) | Claude | Recommended Update |
|----------|------------------|--------|--------------------|
| DynamoDB event store | ✅ | ✅ | keep |
| Same API as Cosmos | ✅ (partial) | ✅ | add WithAspire + idempotency + limit clarity |
| Docs for setup | ✅ (basic plan) | ✅ (examples) | add AWS + LocalStack + S3 offload notes |

---

## 6. Proposed Update Plan for Codex Design

1. **Add a “DynamoDB limits” section** (400KB item, 100 transaction items, GSI eventual consistency).
2. **Adopt Claude’s tag SK and key prefix strategy**.
3. **Introduce idempotency tokens** in WriteEventsAsync description.
4. **Add optional S3 offload path** for projection state store.
5. **Add GSI sharding option** to prevent hot partition.
6. **Expand documentation plan** with concrete AWS/IAM/LocalStack examples.

---

## 7. Conclusion
The Codex design is structurally sound and Cosmos‑aligned, but Claude’s design adds practical DynamoDB‑specific concerns (item limits, idempotency, sharding, S3 offload, alternative trade‑offs). Incorporating those items will make the design more production‑ready while still satisfying the Issue 876 acceptance criteria.

