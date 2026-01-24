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
