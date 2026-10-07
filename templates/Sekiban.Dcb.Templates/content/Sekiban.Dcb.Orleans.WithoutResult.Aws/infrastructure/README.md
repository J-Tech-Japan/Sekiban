# SekibanDcbOrleansAws Infrastructure

AWS CDK Infrastructure for deploying SekibanDcbOrleansAws with DynamoDB.

## Architecture

```mermaid
flowchart TD
    CloudFront --> ALB
    ALB --> Web
    Web[Frontend on ECS] -->|Cloud Map HTTP| API[API and Orleans silo on ECS]
    API --> DynamoDB[Durable events in DynamoDB]
    API --> S3[Snapshots in S3]
    API --> RDS[RDS: cluster membership and reminders]
    API --> Memory[In-memory streams and grain storage]
```

The shipped CDK stack sets `Orleans__UseInMemoryStreams=true`: both Orleans stream providers, PubSubStore and grain storage are in memory. Restarting a silo loses queued stream delivery, subscriptions and grain state; durable DCB events remain in DynamoDB and projections can rebuild from them. This configuration does not provide durable stream delivery across restarts or multi-silo stream routing. SQS is provisioned as a reserved resource but is not used. RDS backs cluster membership and reminders, not grain state. Configure and verify durable streaming and grain persistence before relying on those guarantees in production.

The local AppHost also supplies a PostgreSQL database for the MV sample. The deployed stack does not supply `ConnectionStrings__DcbMaterializedViewPostgres`; provide a separate MV target connection and schema permissions to enable that sample in a cloud deployment.

## Components

| Service | Purpose |
|---------|---------|
| CloudFront | HTTPS frontend with caching disabled |
| ALB | HTTP load balancer for Web service |
| RDS PostgreSQL | Orleans cluster membership and reminders; grain state is in memory |
| DynamoDB | DCB Event Store (auto-created by app) |
| SQS | Provisioned but unused by the application; streams are in memory |
| S3 | Snapshot Offload |
| ECS Fargate (API) | Orleans Silos + REST API (internal only) |
| ECS Fargate (Web) | Blazor Server Frontend (external) |
| Cloud Map | Internal Service Discovery (Web → API) |

## Quick Start

### 1. Install dependencies

```bash
npm install
```

### 2. Configure

```bash
cp config/dev.sample.json config/dev.json
# Edit config/dev.json with your settings
```

### 3. Deploy

```bash
./scripts/deploy.sh dev
```

## Local Development

Run locally with Aspire and LocalStack:

```bash
cd ../SekibanDcbOrleansAws.AppHost
dotnet run
```

## Cost Estimate (Dev Environment)

| Component | Monthly Cost (Est.) |
|-----------|---------------------|
| ECS Fargate API | ~$28-40 |
| ECS Fargate Web | ~$8-12 |
| RDS PostgreSQL | ~$12 |
| ALB | ~$18-24 |
| CloudFront | ~$1-5 |
| DynamoDB/S3/SQS | ~$0-10 |
| **Total** | **~$67-108** |
