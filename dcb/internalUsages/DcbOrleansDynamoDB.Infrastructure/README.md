# Sekiban DynamoDB Infrastructure

AWS CDK Infrastructure for deploying Sekiban DCB + Orleans with DynamoDB.

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

- **CloudFront**: HTTPS termination with default domain (*.cloudfront.net)
- **ALB**: Internal HTTP load balancer for Web service
- **Internal Communication**: Cloud Map + HTTP (no internal ALB)

## Components

| Service | Purpose |
|---------|---------|
| CloudFront | HTTPS frontend with caching disabled |
| ALB | HTTP load balancer for Web service |
| RDS PostgreSQL | Orleans Clustering, Grain State, Reminders |
| DynamoDB | DCB Event Store (auto-created by app) |
| SQS | Orleans Streams (message queues for grain communication) |
| S3 | Snapshot Offload |
| ECS Fargate (API) | Orleans Silos + REST API (internal only) |
| ECS Fargate (Web) | Blazor Server Frontend (external) |
| Cloud Map | Internal Service Discovery (Web → API) |

## URL Routing

| Path | Target |
|------|--------|
| `/*` (all) | Web Service |

**Note**: API is internal only by default. External API access can be configured separately if needed.

## Prerequisites

- AWS CLI configured with appropriate credentials
- Node.js 20+
- AWS CDK CLI (`npm install -g aws-cdk`)
- Docker (for building container images)

## Quick Start

### 1. Install dependencies

```bash
cd dcb/internalUsages/DcbOrleansDynamoDB.Infrastructure
npm install
```

### 2. Bootstrap CDK (first time only)

```bash
npx cdk bootstrap aws://ACCOUNT_ID/REGION
```

### 3. Deploy

```bash
# Development environment
./scripts/deploy.sh dev

# Production environment
./scripts/deploy.sh prod
```

### 4. Build and push container images

```bash
# Build and push both API and Web
./scripts/build-push.sh dev all

# Build and push API only
./scripts/build-push.sh dev api

# Build and push Web only
./scripts/build-push.sh dev web
```

## Configuration

Environment-specific configurations are in `config/`:

- `config/dev.sample.json` - Development environment template
- `config/prod.sample.json` - Production environment template

### Setup

Copy the sample files to create your local configuration:

```bash
cp config/dev.sample.json config/dev.json
cp config/prod.sample.json config/prod.json
```

Edit the JSON files to match your AWS account settings. These files are ignored by git.

### HTTPS (CloudFront)

HTTPS is enabled by default via CloudFront. The CloudFront distribution uses the default `*.cloudfront.net` domain, so no custom domain or ACM certificate is required.

If you want to use a custom domain:
1. Create an ACM certificate in `us-east-1` (required for CloudFront)
2. Add a CNAME record pointing to the CloudFront distribution
3. Update the CDK stack to use the custom domain

### Key Configuration Options

```json
{
  "ecs": {
    "cpu": 512,           // API: vCPU units (512 = 0.5 vCPU)
    "memory": 1024,       // API: Memory in MB
    "desiredCount": 2,    // API: Number of tasks
    "minCount": 1,        // API: Auto-scaling minimum
    "maxCount": 2         // API: Auto-scaling maximum
  },
  "web": {
    "cpu": 256,           // Web: vCPU units
    "memory": 512,        // Web: Memory in MB
    "desiredCount": 1,    // Web: Number of tasks
    "minCount": 1,        // Web: Auto-scaling minimum
    "maxCount": 2,        // Web: Auto-scaling maximum
    "httpPort": 8081      // Web: HTTP port
  },
  "orleans": {
    "siloPort": 11111,    // Silo-to-Silo communication
    "gatewayPort": 30000, // Client-to-Silo gateway
    "httpPort": 8080      // HTTP API port
  }
}
```

## Local Development

Both services support local development and AWS deployment:

### Local (Aspire + LocalStack)

```bash
cd dcb/internalUsages/DcbOrleansDynamoDB.AppHost
dotnet run
```

Environment variables set by Aspire:
- `DynamoDb:ServiceUrl` - LocalStack endpoint
- `Orleans:UseInMemoryStreams=true` - Use in-memory streams
- Service Discovery: `https+http://apiservice` resolves automatically

### AWS Deployment

Environment variables set by ECS:
- `Orleans:UseInMemoryStreams=false` - Use SQS streams
- `RDS_HOST`, `RDS_PORT`, `RDS_USERNAME`, `RDS_PASSWORD`, `RDS_DATABASE` - RDS connection details
- `AWS:Region` - AWS region
- `services__apiservice__http__0` - API service URL for Web (via Cloud Map HTTP)

## External Access

The Web service is accessible externally via CloudFront (HTTPS):

```bash
# Get CloudFront URL from CDK outputs
CF_URL=$(aws cloudformation describe-stacks --stack-name SekibanDynamoDB-dev \
  --query "Stacks[0].Outputs[?OutputKey=='CloudFrontUrl'].OutputValue" --output text)

# Access Web (HTTPS)
curl $CF_URL/
curl $CF_URL/health
curl $CF_URL/weather
```

**Note**: API is internal only by default. For external API access (e.g., for benchmarks), you can customize the CDK stack to add API routes.

## Cost Estimate

| Component | Monthly Cost (Est.) |
|-----------|---------------------|
| ECS Fargate API (2 x 0.5vCPU/1GB) | ~$28-40 |
| ECS Fargate Web (1 x 0.25vCPU/512MB) | ~$8-12 |
| RDS PostgreSQL (db.t4g.micro) | ~$12 |
| ALB | ~$18-24 |
| CloudFront (PriceClass_200) | ~$1-5 |
| SQS (Orleans Streams) | ~$0-5 |
| DynamoDB/S3 | ~$0-10 |
| **Total** | **~$67-108** |

## Useful Commands

```bash
# Synthesize CloudFormation template
npx cdk synth -c env=dev

# Show diff before deployment
npx cdk diff -c env=dev

# Deploy
npx cdk deploy -c env=dev

# Destroy (be careful!)
npx cdk destroy -c env=dev
```

## Orleans Schema Setup

The Orleans PostgreSQL schema is **automatically initialized** at application startup. The API service checks if the schema exists and creates it if needed.

The schema initialization:
- Runs embedded SQL scripts (PostgreSQL-Main.sql, PostgreSQL-Clustering.sql, etc.)
- Is idempotent - safe to run multiple times
- Includes all required Orleans 10.x queries

### Manual Schema Reset

If you need to manually reset the schema:

```bash
# Connect to RDS via bastion or VPN
psql -h <rds-endpoint> -U postgres -d orleans

# Drop and recreate (WARNING: deletes all data)
DROP TABLE IF EXISTS OrleansMembershipTable CASCADE;
DROP TABLE IF EXISTS OrleansMembershipVersionTable CASCADE;
DROP TABLE IF EXISTS OrleansStorage CASCADE;
DROP TABLE IF EXISTS OrleansRemindersTable CASCADE;
DROP TABLE IF EXISTS OrleansQuery CASCADE;
```

The next API service startup will recreate the schema automatically.

## DynamoDB Tables

DynamoDB tables are **automatically created** by the application with the correct schema:

| Table | Partition Key | Sort Key | GSI |
|-------|--------------|----------|-----|
| `sekiban-events-{env}` | pk | sk | GSI1: gsi1pk/sortableUniqueId |
| `sekiban-events-{env}-tags` | pk | sk | GSI1: tagGroup/tagString |
| `sekiban-events-{env}-projections` | pk | sk | - |

The CDK grants IAM permissions for these tables but does not create them.

## SQS Streams (Orleans)

Orleans uses SQS queues for stream communication between grains. The CDK creates the following queues:

| Queue | Purpose |
|-------|---------|
| `{prefix}-0` to `{prefix}-{N-1}` | Stream message queues (configurable count) |
| `{prefix}-dlq` | Dead letter queue for failed messages |

### Configuration

```json
{
  "sqs": {
    "queueNamePrefix": "orleans-stream-dev",  // Queue name prefix
    "queueCount": 4,                          // Number of stream queues
    "visibilityTimeoutSec": 60,               // Message visibility timeout
    "messageRetentionDays": 4                 // Message retention period
  }
}
```

### Local vs AWS

| Environment | Stream Provider |
|-------------|-----------------|
| Local (Aspire) | In-memory streams (`Orleans:UseInMemoryStreams=true`) |
| AWS | SQS streams (`Orleans:UseInMemoryStreams=false`) |

The application automatically selects the appropriate stream provider based on the `Orleans:UseInMemoryStreams` environment variable.
