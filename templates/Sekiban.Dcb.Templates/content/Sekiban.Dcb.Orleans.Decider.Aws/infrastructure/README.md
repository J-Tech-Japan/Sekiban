# SekibanDcbDeciderAws Infrastructure

AWS CDK Infrastructure for deploying SekibanDcbDeciderAws with DynamoDB.

## Architecture

```
        CloudFront (HTTPS)
              │
              ▼
        ALB (HTTP)
              │
              ▼
      ┌─────────────┐
      │ Web Service │
      │ (ECS:8081)  │
      └─────────────┘
              │
              │ Cloud Map (HTTP)
              ▼
      ┌─────────────┐
      │ API Service │◄────► SQS
      │ (ECS:8080)  │   (Orleans Streams)
      └─────────────┘
              │
  ┌───────────┼───────────┐
  ▼           ▼           ▼
RDS Postgres DynamoDB     S3
 (Orleans)   (Events)  (Snapshots)
```

## Components

| Service | Purpose |
|---------|---------|
| CloudFront | HTTPS frontend with caching disabled |
| ALB | HTTP load balancer for Web service |
| RDS PostgreSQL | Orleans Clustering, Grain State, Reminders |
| DynamoDB | DCB Event Store (auto-created by app) |
| SQS | Orleans Streams (reserved for future use) |
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
cd ../SekibanDcbDeciderAws.AppHost
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
