# Sekiban DynamoDB Infrastructure

AWS CDK Infrastructure for deploying Sekiban DCB + Orleans with DynamoDB.

## Architecture

```
          External ALB (HTTP/HTTPS)
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
            │ API Service │
            │ (ECS:8080)  │
            └─────────────┘
                    │
    ┌───────────────┼───────────────┐
    ▼               ▼               ▼
RDS Postgres   DynamoDB            S3
 (Orleans)     (Events)       (Snapshots)
```

- **External ALB**: Web service only (API is internal)
- **Internal Communication**: Cloud Map + HTTP (no internal ALB)

## Components

| Service | Purpose |
|---------|---------|
| RDS PostgreSQL | Orleans Clustering, Grain State, Reminders |
| DynamoDB | DCB Event Store |
| S3 | Snapshot Offload |
| ECS Fargate (API) | Orleans Silos + REST API (internal only) |
| ECS Fargate (Web) | Blazor Server Frontend (external) |
| External ALB | External Access for Web Service |
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

### HTTPS Configuration (External ALB)

To enable HTTPS for external access, you need an ACM certificate. Set the certificate ARN in your config:

```json
{
  "alb": {
    "certificateArn": "arn:aws:acm:ap-northeast-1:123456789012:certificate/xxx",
    "domainName": "example.com"
  }
}
```

When `certificateArn` is set:
- External ALB uses HTTPS (443) with HTTP→HTTPS redirect

When `certificateArn` is empty:
- External ALB uses HTTP only (for development/testing)

**Note**: Internal communication (Web→API) always uses HTTP via Cloud Map.

### Key Configuration Options

```json
{
  "ecs": {
    "cpu": 512,           // API: vCPU units (512 = 0.5 vCPU)
    "memory": 1024,       // API: Memory in MB
    "desiredCount": 2,    // API: Number of tasks
    "minCount": 1,        // API: Auto-scaling minimum
    "maxCount": 4         // API: Auto-scaling maximum
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
- `RdsConnectionString` - RDS PostgreSQL connection string
- `AWS:Region` - AWS region
- `services__apiservice__http__0` - API service URL for Web (via Cloud Map HTTP)

## External Access

The Web service is accessible externally via ALB:

```bash
# Get ALB DNS name from CDK outputs
ALB_DNS=$(aws cloudformation describe-stacks --stack-name SekibanDynamoDbDevStack \
  --query "Stacks[0].Outputs[?OutputKey=='AlbDnsName'].OutputValue" --output text)

# Access Web
curl http://$ALB_DNS/
curl http://$ALB_DNS/health
```

**Note**: API is internal only by default. For external API access (e.g., for benchmarks), you can customize the CDK stack to add API routes to the external ALB.

## Cost Estimate

| Component | Monthly Cost (Est.) |
|-----------|---------------------|
| ECS Fargate API (2 x 0.5vCPU/1GB) | ~$28-40 |
| ECS Fargate Web (1 x 0.25vCPU/512MB) | ~$8-12 |
| RDS PostgreSQL (db.t4g.micro) | ~$12 |
| External ALB | ~$18-24 |
| DynamoDB/S3 | ~$0-10 |
| **Total** | **~$66-98** |

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

Before the first deployment, you need to initialize the Orleans schema in RDS PostgreSQL.

### Option 1: Using the provided script (Recommended)

```bash
# Initialize schema (requires psql and network access to RDS)
./scripts/init-orleans-schema.sh dev

# With SSH tunnel through bastion host
./scripts/init-orleans-schema.sh dev user@bastion-host
```

The script automatically:
- Downloads Orleans PostgreSQL scripts from GitHub
- Retrieves RDS credentials from Secrets Manager
- Runs all required schema scripts

### Option 2: Manual initialization

The schema scripts are available at:
https://github.com/dotnet/orleans/tree/main/src/AdoNet/Shared/PostgreSQL

Run these SQL scripts against your RDS instance:
1. `PostgreSQL-Main.sql` - Clustering and membership tables
2. `PostgreSQL-Persistence.sql` - Grain storage tables
3. `PostgreSQL-Reminders.sql` - Reminder tables

### Network Access to RDS

Since RDS is in a private subnet, you need one of these options:
- VPN/Direct Connect to VPC
- SSH tunnel through a bastion host
- AWS Cloud9 in the same VPC
- Temporarily add your IP to security group (not recommended for production)
