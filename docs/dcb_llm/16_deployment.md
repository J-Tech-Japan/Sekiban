# Deployment Guide - Running DCB in Production

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
> - [Deployment Guide](16_deployment.md) (You are here)

This guide summarizes the moving parts required to deploy a DCB solution and keep it healthy in production. DCB supports both Azure and AWS cloud platforms.

## Platform Comparison

| Component | Azure | AWS |
|-----------|-------|-----|
| Container Hosting | App Service / AKS | ECS Fargate |
| Event Store | Postgres / Cosmos DB | DynamoDB |
| Orleans Clustering | Azure Table / Cosmos DB | RDS PostgreSQL (ADO.NET) |
| Orleans Streams | Azure Queue | Amazon SQS |
| Snapshots | Azure Blob Storage | Amazon S3 |
| CDN | Azure CDN | CloudFront |

## Pre-Deployment Checklist

- ✅ Prepare event store (Postgres/Cosmos/DynamoDB)
- ✅ Configure secrets (connection strings, credentials) in your secret manager
- ✅ Verify `DcbDomainTypes` registration is identical across API, silo, and background services
- ✅ Enable logging, metrics, and distributed tracing (`builder.AddServiceDefaults()` covers Aspire + OTLP setup)

---

## Azure Deployment

### Infrastructure

- **Orleans Cluster** – Deploy silos behind a load balancer. Use Azure App Service, AKS, or container orchestrators.
- **Event Store** – Managed Postgres (Azure Database for PostgreSQL Flexible Server) or Cosmos DB.
- **Azure Storage** – Tables (clustering), Queues (streams), Blobs (grain state + snapshots).
- **Redis** (optional) – For caching API responses or storing session state if you run Blazor Server at scale.

### Configuration

```json
{
  "Sekiban": {
    "Database": "postgres"
  },
  "ConnectionStrings": {
    "SekibanDcb": "Host=...;Database=...;Username=...;Password=..."
  },
  "ORLEANS_CLUSTERING_TYPE": "azuretable",
  "ORLEANS_GRAIN_DEFAULT_TYPE": "blob"
}
```

---

## AWS Deployment

### Infrastructure

AWS uses CDK for infrastructure provisioning:

- **ECS Fargate** – Hosts API/Web containers
- **CloudFront + ALB** – CDN and load balancing
- **DynamoDB** – Event store (tables auto-created)
- **RDS PostgreSQL** – Orleans clustering
- **Amazon SQS** – Orleans streams
- **Amazon S3** – MultiProjection snapshots

### CDK Deployment

```bash
cd dcb/internalUsages/DcbOrleansDynamoDB.Infrastructure

# Create configuration file
cp lib/config/dev.sample.json lib/config/dev.json
# Edit dev.json to set AWS_ACCOUNT_ID and AWS_REGION

# Deploy
./deploy.sh dev
```

### Configuration

```json
{
  "Sekiban": {
    "Database": "dynamodb"
  },
  "AWS_REGION": "us-west-1",
  "DYNAMODB_TABLE_PREFIX": "myapp"
}
```

### AWS-Specific Notes

- DynamoDB tables are auto-created on application startup
- RDS PostgreSQL Orleans schema is auto-created by `OrleansSchemaInitializer`
- SQS queues are provisioned via CDK

## Observability

- Enable OpenTelemetry exporters (e.g., Azure Monitor, Grafana Tempo).
- Log `ExecutionResult` at Information level to track command throughput and conflicts.
- Surface health endpoints (`/health`) for readiness/liveness probes.
- Monitor queue depths for Azure Queue streams and RU consumption for Cosmos.

## Scaling Guidance

- **Orleans** – Scale silos horizontally. Hot tags move automatically; consider pinning by hash range if needed.
- **Postgres** – Use connection pooling and read replicas for analytical workloads.
- **Cosmos** – Enable autoscale RU and monitor partition distribution.
- **Blazor/API** – Deploy separately from the silo for independent scaling; they communicate via Orleans clients.

## Deployment Strategies

1. **Blue/Green** – Spin up a second cluster and switch traffic once projections catch up.
2. **Rolling Upgrade** – Orleans supports rolling restarts; ensure all nodes share the same schema and domain types.
3. **Zero-Downtime Migrations** – Use additive database migrations; rebuild projections after code changes that affect
   projector logic.

## Disaster Recovery

- Back up Postgres regularly (point-in-time recovery).
- Enable Cosmos continuous backup or account-level failover.
- Store projection snapshots in redundant storage; they can be rebuilt from the event log if lost.
- Document manual steps for rehydrating projections and clearing stuck reservations.

## Automation

- Use CI to run `dotnet publish` for API, Web, and Orleans projects.
- Containerize using the template’s Dockerfiles or `dotnet publish /p:UseAppHost=false` for self-contained builds.
- Deploy with GitHub Actions or Azure DevOps using Bicep/Terraform for infrastructure.

## Post-Deployment Validation

- Execute smoke commands (`CreateStudent`) and confirm projections respond with `waitForSortableUniqueId`.
- Check Orleans dashboard for silo membership, activation counts, and queue lag.
- Validate event store metrics (latency, throughput) align with expectations.

Keeping DCB healthy hinges on monitoring reservation conflicts, watching projection lag, and ensuring storage backends are
provisioned for peak throughput. With those practices in place you gain the flexibility of dynamic consistency while
retaining operational confidence.

## Orleans 10.4.2 dependency migration

DCB hosts consuming `Sekiban.Dcb.Orleans.*` must align their direct `Microsoft.Orleans.*` references on 10.4.2 and
upgrade the whole cluster together. The DCB authority keeps the source, internal-use projects, tests, and five
generated template families on that same version. The Orleans 10.4.2 host/runtime packages publish only `net8.0` and `net10.0`
groups: net9 resolves the net8 group with applicable `Microsoft.Extensions.*` 8.0.x floors (for example,
`Microsoft.Extensions.Hosting` 8.0.1), while net10 resolves the net10 group with a 10.0.5 floor. `Polly` 8.6.4
and `Polly.Extensions` 8.6.5 are normal transitives in both groups. Do not mix Orleans versions during a rolling upgrade.

The PostgreSQL schema upgrade below preserves stored state and reminders. This alignment does not make Azure Queue
rewindable, and does not include #1185 subscriber recovery. Pure and Samples are deliberately outside this DCB
upgrade boundary.

> **10.4.2 upgrade:** Upgrade the whole cluster together; mixed Orleans versions are unsupported. AWS template PostgreSQL scripts were refreshed. See [the deployment upgrade steps](16_deployment.md#orleans-1042-postgresql-upgrade). Orleans 10.4.0 changed request-latency instruments and grain metric dimensions; update dashboards and alerts.

<a id="orleans-1042-postgresql-upgrade"></a>

### Orleans 10.4.2 PostgreSQL upgrade

New AWS template databases and the DynamoDB internal host use the v10.4.2 PostgreSQL scripts. Main and Reminders are unchanged; Clustering updates membership concurrency and dead-silo cleanup; Persistence uses `timestamptz`/`now()` and includes `DeleteStorageKey`.

`OrleansSchemaInitializer` does **not** upgrade an existing valid database: it only checks for `orleansquery` and `CleanupDefunctSiloEntriesKey`. Keep those checks intact. Forcing reinitialization drops grain state and reminder tables. Back up the database and stop all cluster nodes, then apply these changes in a transaction without dropping tables:

1. From the shipped `PostgreSQL-Clustering.sql`, execute only the definitions of `update_i_am_alive_time`, `insert_membership`, and `update_membership`, changing `CREATE FUNCTION` to `CREATE OR REPLACE FUNCTION`. Leave the unchanged `insert_membership_version` alone.
2. Replace the existing `CleanupDefunctSiloEntriesKey` row's `QueryText` using `UPDATE OrleansQuery SET QueryText = ... WHERE QueryKey = 'CleanupDefunctSiloEntriesKey'`, taking the complete query string from the refreshed script. It now requires old `IAmAliveTime` **and** `StartTime`, empty `SuspectTimes`, and dead status (`6`). Execute the script's `INSERT` for `CleanupDefunctSiloEntryKey` if absent (or update its query text if already installed).
3. Optionally align the storage timestamp column, interpreting the old values as UTC:

   ```sql
   ALTER TABLE OrleansStorage
     ALTER COLUMN modifiedon TYPE timestamptz
     USING modifiedon AT TIME ZONE 'UTC';
   ```

   Execute the `CREATE OR REPLACE FUNCTION writetostorage` definition from the refreshed Persistence script. If retaining the old timestamp column, keep database sessions in UTC so `now()` is converted consistently. The new `DeleteStorageKey` row can also be inserted if absent; no existing state is deleted merely by registering this query. Do not rerun the full table-creation scripts.
4. Commit, then start the whole cluster on 10.4.2 together. The [10.4.2 query reader](https://github.com/dotnet/orleans/blob/v10.4.2/src/AdoNet/Shared/Storage/DbStoredQueries.cs) treats `CleanupDefunctSiloEntryKey` as optional and [falls back to the installed bulk query](https://github.com/dotnet/orleans/blob/v10.4.2/src/AdoNet/Shared/Storage/RelationalOrleansQueries.cs), so the previous schema can still start; it does not gain the refreshed cleanup behavior until explicitly upgraded.

Orleans 10.4.0 replaced `orleans-app-requests-latency-bucket`, `-count`, and `-sum` with the `orleans-app-requests-latency` histogram in fractional milliseconds. Configure OpenTelemetry View boundaries and update dashboards/alerts. `orleans-grains` replaces the `type` label with canonical `grain_type`; activation lifecycle metrics also gain `grain_type`. See the [10.4.0 upgrade notes](https://github.com/dotnet/orleans/releases/tag/v10.4.0).
