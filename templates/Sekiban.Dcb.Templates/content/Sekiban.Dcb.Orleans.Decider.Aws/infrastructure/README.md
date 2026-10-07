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
      │ WebNext     │
      │ (ECS:3000)  │
      └─────────────┘
              │
              │ Cloud Map (HTTP)
              ▼
      ┌─────────────┐
      │ API Service │◄────► SQS
      │ (ECS:8080)  │   (Orleans Streams)
      └─────────────┘
              │
  ┌───────────┼───────────┬───────────┐
  ▼           ▼           ▼           ▼
RDS Postgres RDS Postgres DynamoDB   S3
 (Orleans)   (Identity)  (Events)  (Snapshots)
```

## Components

| Service | Purpose |
|---------|---------|
| CloudFront | HTTPS frontend with caching disabled |
| ALB | HTTP load balancer for WebNext service |
| RDS PostgreSQL (Orleans) | Orleans Clustering, Grain State, Reminders |
| RDS PostgreSQL (Identity) | ASP.NET Identity (Authentication) |
| DynamoDB | DCB Event Store (auto-created by app) |
| SQS | Orleans Streams (reserved for future use) |
| S3 | Snapshot Offload |
| ECS Fargate (API) | Orleans Silos + REST API (internal only) |
| ECS Fargate (WebNext) | Next.js Frontend (external) |
| Cloud Map | Internal Service Discovery (WebNext → API) |

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

### 3. Build and Push Container Images

```bash
./scripts/build-push.sh dev all
```

### 4. Deploy

```bash
./scripts/deploy.sh dev
```

## Scripts

| Script | Purpose |
|--------|---------|
| `scripts/deploy.sh dev` | Deploy to dev environment |
| `scripts/deploy.sh prod` | Deploy to production environment |
| `scripts/build-push.sh dev all` | Build and push all container images |
| `scripts/build-push.sh dev api` | Build and push API service only |
| `scripts/build-push.sh dev webnext` | Build and push WebNext service only |

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
| ECS Fargate WebNext | ~$8-12 |
| RDS PostgreSQL (Orleans) | ~$12 |
| RDS PostgreSQL (Identity) | ~$12 |
| ALB | ~$18-24 |
| CloudFront | ~$1-5 |
| DynamoDB/S3/SQS | ~$0-10 |
| **Total** | **~$79-115** |

Before deploying the API, supply `Jwt__SecretKey` (at least 32 characters) from a secret store. For Azure, add an App Service Key Vault reference or a Container Apps secret reference to that environment variable; for ECS, add a Secrets Manager reference in the task definition's `secrets`. The shipped infrastructure does not provision this secret. Startup fails without it. Sample users are created only in Development; to bootstrap the first administrator outside Development, also supply `Auth__InitialAdmin__Email` and `Auth__InitialAdmin__Password` from secrets. An existing account is never promoted by this setting. Identity tables and roles are initialized in every environment.
