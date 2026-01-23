# AWS Infrastructure Design for Sekiban DCB + Orleans

## Overview

This document proposes AWS infrastructure configurations for running Sekiban DCB with Microsoft Orleans, optimized for small to medium-sized systems with predictable costs.

### Requirements
- **Orleans official packages only** (no third-party providers)
- **AWS managed services** preferred for reduced operational overhead
- **Cost stability** for small/medium workloads
- **Comparable to Azure pricing** (~$50/month minimum, equivalent to Azure App Service B1/B2)
- **Scale-out support** (multiple silos required, Memory Streams not viable)

---

## Cost Comparison: Azure vs AWS

### Azure Reference (from user)
| Service | Daily Cost | Monthly Cost |
|---------|------------|--------------|
| Azure Container Apps | ¥500-1,000 | ¥15,000-30,000 |
| App Service B1/B2 | ¥250~ | ¥7,500~ |

### Target: ~$50/month (~¥7,500) for minimum viable deployment

---

## Recommended Architecture: Option A - Full AWS Native (DynamoDB + SQS)

**Recommended for scale-out deployments**

### Architecture Diagram
```
                    ┌──────────────────────────────────────────────────────────────┐
                    │                          AWS Cloud                           │
                    │                                                              │
                    │    ┌──────────┐                                             │
                    │    │   ALB    │ ← HTTPS                                     │
                    │    └────┬─────┘                                             │
                    │         │                                                   │
                    │    ┌────▼────────────────────────────────────┐              │
                    │    │           ECS Fargate Cluster           │              │
                    │    │  ┌─────────────┐    ┌─────────────┐    │              │
                    │    │  │ Orleans     │    │ Orleans     │    │              │
                    │    │  │ Silo #1     │    │ Silo #2     │ .. │  Scale-out   │
                    │    │  │ + API       │    │ + API       │    │              │
                    │    │  └─────────────┘    └─────────────┘    │              │
                    │    └────────────┬────────────────────────────┘              │
                    │                 │                                           │
                    │    ┌────────────┼────────────┬───────────┐                 │
                    │    ▼            ▼            ▼           ▼                 │
                    │ ┌───────┐  ┌───────┐   ┌─────────┐  ┌──────┐              │
                    │ │DynamoDB│  │DynamoDB│   │   SQS   │  │  S3  │              │
                    │ │(Events)│  │(Orleans)│  │(Streams)│  │(Snap)│              │
                    │ └───────┘  └───────┘   └─────────┘  └──────┘              │
                    │                                                              │
                    └──────────────────────────────────────────────────────────────┘
```

### Component Details

| Component | Service | Purpose | Monthly Cost (Est.) |
|-----------|---------|---------|---------------------|
| Compute | ECS Fargate (0.5 vCPU / 1GB) x 2 | Orleans Silos + API | ~$28-40 |
| Orleans Clustering | DynamoDB On-Demand | Membership Table | ~$0-2 (free tier) |
| Orleans Grain State | DynamoDB On-Demand | Grain Persistence | ~$0-5 (free tier) |
| Orleans Reminders | DynamoDB On-Demand | Timer/Reminders | ~$0-2 (free tier) |
| **Orleans Streams** | **SQS Standard** | **Persistent Streams** | **~$0-1 (1M free/month)** |
| Event Store | DynamoDB On-Demand | DCB Events | ~$0-5 (free tier) |
| Snapshots | S3 Standard | Large State Offload | ~$0-2 |
| Load Balancer | ALB | External Access | ~$18-24 |
| Logs | CloudWatch Logs | Monitoring | ~$1-3 |
| **Total (2 silos)** | | | **~$47-84/month** |

### Orleans Configuration
```csharp
// Using official Orleans AWS packages - Scale-out ready
siloBuilder
    // Clustering via DynamoDB
    .UseDynamoDBClustering(options => {
        options.Service = "us-east-1";
        options.TableName = "OrleansCluster";
        options.CreateIfNotExists = true;
    })
    // Grain State via DynamoDB
    .AddDynamoDBGrainStorage("OrleansStorage", options => {
        options.Service = "us-east-1";
        options.TableName = "OrleansGrainState";
        options.CreateIfNotExists = true;
    })
    // Reminders via DynamoDB
    .UseAdoNetReminderService(options => { /* or custom DynamoDB */ })
    // Streams via SQS (scale-out compatible)
    .AddSqsStreams("SQSProvider", options => {
        options.Service = "us-east-1";
    });
```

### NuGet Packages (All Official Microsoft)
- `Microsoft.Orleans.Clustering.DynamoDB`
- `Microsoft.Orleans.Persistence.DynamoDB`
- `Microsoft.Orleans.Reminders.DynamoDB`
- `Microsoft.Orleans.Streaming.SQS`

---

## Network Configuration (ECS Fargate)

### Orleans Port Requirements

| Port | Purpose | Exposure |
|------|---------|----------|
| **11111** | Silo-to-Silo communication | Internal only (VPC) |
| **30000** | Gateway (Client-to-Silo) | Internal only (VPC) |
| **8080** | HTTP API | External (via ALB) |

### Architecture with Ports
```
                 Internet
                    │
                    ▼
              ┌──────────┐
              │   ALB    │ ← Port 443/80 (HTTPS/HTTP)
              └────┬─────┘
                   │ Port 8080 only
                   ▼
    ┌──────────────────────────────────────────────────┐
    │                  VPC (Private Subnet)            │
    │                                                  │
    │   ┌─────────────────┐    ┌─────────────────┐    │
    │   │   ECS Task #1   │    │   ECS Task #2   │    │
    │   │  ┌───────────┐  │    │  ┌───────────┐  │    │
    │   │  │  Orleans  │  │    │  │  Orleans  │  │    │
    │   │  │  Silo     │  │    │  │  Silo     │  │    │
    │   │  └───────────┘  │    │  └───────────┘  │    │
    │   │   :11111 (silo) │◄──►│   :11111 (silo) │    │
    │   │   :30000 (gw)   │◄──►│   :30000 (gw)   │    │
    │   │   :8080 (http)  │    │   :8080 (http)  │    │
    │   │   10.0.1.10     │    │   10.0.1.11     │    │
    │   └─────────────────┘    └─────────────────┘    │
    │           │                      │              │
    │           └──────────┬───────────┘              │
    │                      ▼                          │
    │            ┌──────────────────┐                 │
    │            │   Cloud Map      │                 │
    │            │ (Service Discov) │                 │
    │            └──────────────────┘                 │
    │                                                  │
    └──────────────────────────────────────────────────┘
```

### Security Group Configuration

#### ALB Security Group
```hcl
# Inbound: Internet → ALB
ingress {
  from_port   = 443
  to_port     = 443
  protocol    = "tcp"
  cidr_blocks = ["0.0.0.0/0"]  # Public access
}
```

#### ECS Task Security Group
```hcl
# Inbound: ALB → ECS (HTTP API only)
ingress {
  from_port       = 8080
  to_port         = 8080
  protocol        = "tcp"
  security_groups = [alb_security_group_id]
}

# Inbound: ECS ↔ ECS (Orleans Silo-to-Silo)
ingress {
  from_port = 11111
  to_port   = 11111
  protocol  = "tcp"
  self      = true  # Same security group
}

# Inbound: ECS ↔ ECS (Orleans Gateway)
ingress {
  from_port = 30000
  to_port   = 30000
  protocol  = "tcp"
  self      = true  # Same security group
}
```

### ECS Task Definition (Multiple Ports)
```json
{
  "containerDefinitions": [
    {
      "name": "orleans-silo",
      "portMappings": [
        {
          "containerPort": 8080,
          "protocol": "tcp",
          "name": "http"
        },
        {
          "containerPort": 11111,
          "protocol": "tcp",
          "name": "silo"
        },
        {
          "containerPort": 30000,
          "protocol": "tcp",
          "name": "gateway"
        }
      ]
    }
  ]
}
```

### Service Discovery (AWS Cloud Map)

ECS Service Discovery を使用して、Orleans silo が互いを発見できるようにします。

```hcl
resource "aws_service_discovery_service" "orleans" {
  name = "orleans-silo"

  dns_config {
    namespace_id = aws_service_discovery_private_dns_namespace.main.id

    dns_records {
      ttl  = 10
      type = "A"
    }

    routing_policy = "MULTIVALUE"
  }
}
```

Orleans 側で DNS 名を使用してクラスタリング:
- `orleans-silo.internal` → 全 silo の IP アドレスを返す
- DynamoDB Membership Table で正確な IP/Port を管理

### Orleans Endpoint Configuration
```csharp
siloBuilder.Configure<EndpointOptions>(options =>
{
    options.SiloPort = 11111;        // Silo-to-Silo
    options.GatewayPort = 30000;     // Client-to-Silo (Gateway)

    // ECS Fargate では自動的にプライベート IP を取得
    options.AdvertisedIPAddress = GetPrivateIpAddress();
});
```

### Key Points
1. **ALB は HTTP (8080) のみ** を外部公開
2. **Silo ポート (11111) と Gateway ポート (30000)** は VPC 内部のみ
3. **Security Group の `self = true`** で同一 SG 内の通信を許可
4. **Cloud Map** でサービスディスカバリ（DNS ベース）
5. **DynamoDB Clustering** で正確なメンバーシップ管理

---

## Alternative Architecture: Option B - ECS Fargate + RDS PostgreSQL + SQS

### Concept
Use RDS PostgreSQL for Orleans clustering/state, SQS for streams. More traditional setup.

### Architecture
```
                    ┌──────────────────────────────────────────────────────┐
                    │                      AWS Cloud                       │
                    │                                                      │
                    │    ┌────────────────────────────────────┐           │
                    │    │        ECS Fargate Cluster         │           │
                    │    │  ┌──────────┐    ┌──────────┐     │           │
                    │    │  │ Silo #1  │    │ Silo #2  │ ..  │           │
                    │    │  └──────────┘    └──────────┘     │           │
                    │    └──────────────┬─────────────────────┘           │
                    │                   │                                 │
                    │    ┌──────────────┼──────────────┬──────────┐      │
                    │    ▼              ▼              ▼          ▼      │
                    │ ┌──────┐    ┌───────┐     ┌─────────┐  ┌──────┐   │
                    │ │ RDS  │    │DynamoDB│     │   SQS   │  │  S3  │   │
                    │ │Postgres│  │(Events)│     │(Streams)│  │(Snap)│   │
                    │ │(Orleans)│ │        │     │         │  │      │   │
                    │ └──────┘    └───────┘     └─────────┘  └──────┘   │
                    │                                                      │
                    └──────────────────────────────────────────────────────┘
```

### Component Costs

| Component | Service | Monthly Cost (Est.) |
|-----------|---------|---------------------|
| Compute | ECS Fargate (0.5 vCPU / 1GB) x 2 | ~$28-40 |
| Orleans Clustering/State | RDS PostgreSQL (db.t4g.micro) | ~$12 (free tier 1st year) |
| Orleans Streams | SQS Standard | ~$0-1 (1M free/month) |
| Event Store | DynamoDB On-Demand | ~$0-5 |
| Snapshots | S3 | ~$0-2 |
| Load Balancer | ALB | ~$18-24 |
| **Total (2 silos)** | | **~$58-84/month** |

### Orleans Configuration
```csharp
// Using official Orleans packages - RDS + SQS
siloBuilder
    .UseAdoNetClustering(options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    .AddAdoNetGrainStorage("OrleansStorage", options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    .UseAdoNetReminderService(options => {
        options.ConnectionString = rdsConnectionString;
        options.Invariant = "Npgsql";
    })
    // Streams via SQS (scale-out compatible)
    .AddSqsStreams("SQSProvider", options => {
        options.Service = "us-east-1";
    });
```

### NuGet Packages (Official)
- `Microsoft.Orleans.Clustering.AdoNet`
- `Microsoft.Orleans.Persistence.AdoNet`
- `Microsoft.Orleans.Reminders.AdoNet`
- `Microsoft.Orleans.Streaming.SQS`
- `Npgsql`

### Pros
- Well-established RDS PostgreSQL reliability
- AdoNet providers are mature and battle-tested
- Fixed monthly cost for RDS (predictable)

### Cons
- RDS adds ~$12/month fixed cost even in free tier after 1st year
- Two database technologies to manage (RDS + DynamoDB)

---

## Alternative Architecture: Option C - App Runner (Dev/Small Scale Only)

### Concept
Use AWS App Runner for maximum simplicity. **NOT recommended for production scale-out.**

### Architecture
```
                    ┌─────────────────────────────────────────┐
                    │              AWS Cloud                  │
                    │                                         │
                    │    ┌──────────────────────┐            │
                    │    │    App Runner        │ ← Built-in │
                    │    │  Orleans Silo + API  │    HTTPS   │
                    │    └──────────┬───────────┘            │
                    │               │                        │
                    │    ┌──────────┼──────────┬──────────┐ │
                    │    ▼          ▼          ▼          ▼ │
                    │ ┌───────┐ ┌───────┐  ┌──────┐ ┌──────┐│
                    │ │DynamoDB│ │DynamoDB│  │ SQS  │ │  S3  ││
                    │ │(Events)│ │(Orleans)│ │      │ │(Snap)││
                    │ └───────┘ └───────┘  └──────┘ └──────┘│
                    │                                        │
                    └────────────────────────────────────────┘
```

### Component Costs (Single Instance)

| Component | Service | Monthly Cost (Est.) |
|-----------|---------|---------------------|
| Compute | App Runner (0.5 vCPU / 1GB) | ~$10-20 (idle = ~$5) |
| Orleans | DynamoDB On-Demand | ~$0-5 |
| Streams | SQS Standard | ~$0-1 |
| Event Store | DynamoDB On-Demand | ~$0-5 |
| Snapshots | S3 | ~$0-2 |
| **No ALB needed** | Built into App Runner | $0 |
| **Total** | | **~$10-33/month** |

### ⚠️ Scale-out Limitations
- App Runner instances cannot directly communicate (no silo-to-silo mesh)
- Orleans clustering requires stable endpoint discovery
- **Use for development/testing only, not production scale-out**

### Pros
- No ALB cost (HTTPS included)
- Lowest cost for dev/test
- Simplest deployment model

### Cons
- **NOT suitable for multi-silo production deployment**
- Limited networking control for Orleans gossip
- Scale-out architecture fundamentally incompatible

---

## Cost Optimization Strategies

### 1. Use Free Tier Aggressively
| Service | Free Tier | Duration |
|---------|-----------|----------|
| RDS PostgreSQL | 750 hrs/month (db.t4g.micro) | First 12 months |
| DynamoDB | 25 RCU, 25 WCU, 25GB storage | **Permanent** |
| **SQS** | **1 million requests/month** | **Permanent** |
| S3 | 5GB storage | First 12 months |
| CloudWatch | 5GB logs | Permanent |

### SQS Pricing Details (After Free Tier)
| Queue Type | Price per Million Requests |
|------------|---------------------------|
| Standard Queue | $0.40 |
| FIFO Queue | $0.50 |

> **Note:** For Orleans streaming with moderate traffic, SQS cost is typically negligible (~$0-5/month)

### 2. Use Graviton (ARM) Instances
- Fargate ARM: **20% cheaper** than x86
- RDS db.t4g: ARM-based, better price/performance

### 3. Reserved Capacity (Long-term)
- Fargate Savings Plans: Up to **50% off**
- RDS Reserved Instances: Up to **40% off**

### 4. Remove ALB for Dev/Small Prod
- Use direct Fargate Service Connect or Cloud Map
- App Runner includes HTTPS endpoint

### 5. DynamoDB On-Demand (Default)
- Since Nov 2024, 50% price reduction
- Now recommended over Provisioned for most workloads

---

## Recommendation Summary

| Scenario | Recommended Option | Est. Monthly Cost | Scale-out |
|----------|-------------------|-------------------|-----------|
| **Dev / Test** | Option C (App Runner) | ~$10-33 | ❌ Single only |
| **Production** | Option A (DynamoDB + SQS) | ~$47-84 | ✅ Multi-silo |
| **Production (RDS preference)** | Option B (RDS + SQS) | ~$58-84 | ✅ Multi-silo |

### Primary Recommendation: **Option A (Full AWS Native: DynamoDB + SQS)**

**Rationale:**
1. Uses **all official Orleans AWS packages**:
   - `Microsoft.Orleans.Clustering.DynamoDB`
   - `Microsoft.Orleans.Persistence.DynamoDB`
   - `Microsoft.Orleans.Streaming.SQS`
2. **Scale-out ready** with SQS persistent streams
3. Single database technology (DynamoDB) reduces operational complexity
4. DynamoDB + SQS both have generous free tiers
5. Comparable to Azure Container Apps pricing (~¥7,500-12,000/month for 2 silos)
6. All services are serverless/pay-per-use

---

## Implementation Checklist

### Phase 1: Local Development (Current)
- [x] DynamoDB event store implemented
- [x] S3 snapshot offload implemented
- [x] LocalStack for local testing
- [x] Orleans localhost clustering (dev mode)
- [ ] Orleans DynamoDB clustering integration
- [ ] Orleans SQS streaming integration

### Phase 2: AWS Deployment (Scale-out Ready)
- [ ] Create ECS Fargate task definition (multi-task, 3 ports)
  - [ ] Port 8080: HTTP API (ALB target)
  - [ ] Port 11111: Silo-to-Silo
  - [ ] Port 30000: Gateway (Client-to-Silo)
- [ ] Configure DynamoDB tables for Orleans:
  - [ ] `OrleansCluster` (membership)
  - [ ] `OrleansGrainState` (persistence)
  - [ ] `OrleansReminders` (reminders)
- [ ] Create SQS queues for Orleans streaming
- [ ] Set up VPC / Security Groups:
  - [ ] ALB SG: Inbound 443 from Internet
  - [ ] ECS SG: Inbound 8080 from ALB SG
  - [ ] ECS SG: Inbound 11111, 30000 from self (silo communication)
- [ ] Configure ALB (HTTP only, port 8080)
- [ ] Set up Cloud Map for service discovery
- [ ] Configure CloudWatch logging
- [ ] Set up Secrets Manager for credentials

### Phase 3: Production Ready
- [ ] Enable DynamoDB point-in-time recovery
- [ ] Configure ECS auto-scaling policies (min 2, max N)
- [ ] Set up CloudWatch alarms
- [ ] Implement backup strategy
- [ ] Configure SQS dead-letter queues

---

## References

### AWS Pricing
- [AWS Fargate Pricing](https://aws.amazon.com/fargate/pricing/)
- [AWS App Runner Pricing](https://aws.amazon.com/apprunner/pricing/)
- [AWS DynamoDB Pricing](https://aws.amazon.com/dynamodb/pricing/on-demand/)
- [AWS SQS Pricing](https://aws.amazon.com/sqs/pricing/)
- [AWS RDS PostgreSQL Pricing](https://aws.amazon.com/rds/postgresql/pricing/)
- [AWS ALB Pricing](https://aws.amazon.com/elasticloadbalancing/pricing/)

### Orleans AWS Packages (Official Microsoft)
- [Microsoft.Orleans.Clustering.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.DynamoDB)
- [Microsoft.Orleans.Persistence.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB)
- [Microsoft.Orleans.Reminders.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.DynamoDB)
- [Microsoft.Orleans.Streaming.SQS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS)

### Implementation Guides
- [Running Orleans on AWS ECS](https://medium.com/@Sas_Amir/running-ms-orleans-cluster-in-aws-ecs-88edaaf4564b)
- [Orleans NuGet Packages (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/orleans/resources/nuget-packages)
