# AWS Infrastructure as Code Design for Sekiban DCB + Orleans

## Overview

This document defines the IaC approach for deploying Sekiban DCB + Orleans on AWS, matching the Azure Bicep structure used in the Kenbai project.

### Goals
1. **Local deployment**: Deploy from developer machines using CLI
2. **CLI + IaC**: Infrastructure defined as code, deployed via AWS CLI/SDK
3. **GitHub Actions**: Automated CI/CD pipeline for continuous deployment
4. **Environment parity**: Support dev/stg/prod environments with parameterization

---

## IaC Tool Comparison

| Feature | AWS CDK (C#) | Terraform | CloudFormation |
|---------|-------------|-----------|----------------|
| Language | C#, TypeScript, Python | HCL | YAML/JSON |
| .NET Affinity | ✅ Excellent | ⚠️ Separate | ⚠️ Separate |
| Module Reuse | ✅ Constructs | ✅ Modules | ⚠️ Nested Stacks |
| State Management | CloudFormation | Terraform State | CloudFormation |
| Multi-cloud | ❌ AWS only | ✅ Yes | ❌ AWS only |
| Learning Curve | Low (for .NET devs) | Medium | Medium |
| Bicep Equivalent | ✅ Very similar | Similar | Similar |

### Recommendation: **AWS CDK (C#)**

**Rationale:**
- .NET developers can use familiar C# syntax
- Similar modular structure to Bicep
- Type-safe infrastructure definitions
- Generates CloudFormation under the hood (safe, auditable)
- Excellent IDE support in Rider/VS

---

## Directory Structure

```
Infrastructure/
├── AwsEcsFargate/
│   ├── src/
│   │   └── AwsEcsFargate/
│   │       ├── AwsEcsFargateStack.cs          # Main stack (like main.bicep)
│   │       ├── Program.cs                      # CDK app entry point
│   │       ├── Constructs/                     # Reusable modules (like modules/)
│   │       │   ├── VpcConstruct.cs
│   │       │   ├── DynamoDbConstruct.cs
│   │       │   ├── SqsConstruct.cs
│   │       │   ├── S3Construct.cs
│   │       │   ├── EcsFargateConstruct.cs
│   │       │   ├── AlbConstruct.cs
│   │       │   ├── CloudMapConstruct.cs
│   │       │   └── SecretsManagerConstruct.cs
│   │       └── AwsEcsFargate.csproj
│   │
│   ├── parameters/                             # Environment configs (like parameters/)
│   │   ├── dev.json
│   │   ├── stg.json
│   │   └── prod.json
│   │
│   ├── scripts/                                # Deployment scripts (like scripts/)
│   │   ├── deploy-all.sh
│   │   ├── deploy-infra.sh
│   │   ├── deploy-apps.sh
│   │   ├── build-push-images.sh
│   │   └── destroy.sh
│   │
│   ├── dev.local.json                          # Local config (gitignored)
│   ├── dev.local.json.sample                   # Sample config
│   ├── cdk.json                                # CDK configuration
│   └── README.md
│
└── .github/
    └── workflows/
        ├── aws-deploy-dev.yml
        ├── aws-deploy-stg.yml
        └── aws-deploy-prod.yml
```

---

## Configuration Files

### dev.local.json.sample
```json
{
  "account": "123456789012",
  "region": "ap-northeast-1",
  "environment": "dev",
  "projectName": "sekiban-dcb",

  "vpc": {
    "cidr": "10.0.0.0/16",
    "maxAzs": 2
  },

  "ecs": {
    "clusterName": "sekiban-cluster",
    "serviceName": "orleans-silo",
    "desiredCount": 2,
    "cpu": 512,
    "memory": 1024,
    "minCapacity": 2,
    "maxCapacity": 10
  },

  "orleans": {
    "clusterId": "sekiban-cluster-dev",
    "serviceId": "sekiban-service-dev",
    "siloPort": 11111,
    "gatewayPort": 30000,
    "httpPort": 8080
  },

  "dynamodb": {
    "eventsTableName": "sekiban-events",
    "orleansClusterTableName": "orleans-cluster",
    "orleansGrainStateTableName": "orleans-grain-state",
    "orleansRemindersTableName": "orleans-reminders",
    "billingMode": "PAY_PER_REQUEST"
  },

  "sqs": {
    "queueNamePrefix": "orleans-stream"
  },

  "s3": {
    "snapshotBucketName": "sekiban-snapshots-dev"
  },

  "containerImage": {
    "repository": "sekiban-api",
    "tag": "latest"
  }
}
```

### parameters/dev.json (CDK context)
```json
{
  "environment": "dev",
  "projectName": "sekiban-dcb",
  "ecs": {
    "desiredCount": 2,
    "cpu": 512,
    "memory": 1024
  },
  "dynamodb": {
    "billingMode": "PAY_PER_REQUEST"
  }
}
```

---

## CDK Stack Implementation

### Program.cs
```csharp
using Amazon.CDK;

namespace AwsEcsFargate;

sealed class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        // Read environment from context or default to "dev"
        var environment = app.Node.TryGetContext("environment")?.ToString() ?? "dev";
        var config = LoadConfig(environment);

        new AwsEcsFargateStack(app, $"SekibanDcb-{environment}", new AwsEcsFargateStackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = config.Account,
                Region = config.Region
            },
            Config = config
        });

        app.Synth();
    }

    private static StackConfig LoadConfig(string environment)
    {
        var configPath = $"parameters/{environment}.json";
        // Load and parse configuration
        // ...
    }
}
```

### AwsEcsFargateStack.cs (Main Stack)
```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.S3;
using Constructs;
using AwsEcsFargate.Constructs;

namespace AwsEcsFargate;

public class AwsEcsFargateStack : Stack
{
    public AwsEcsFargateStack(Construct scope, string id, AwsEcsFargateStackProps props)
        : base(scope, id, props)
    {
        var config = props.Config;

        // ============================================================
        // 1. VPC (like modules/4.vnet/create.bicep)
        // ============================================================
        var vpcConstruct = new VpcConstruct(this, "Vpc", new VpcConstructProps
        {
            Cidr = config.Vpc.Cidr,
            MaxAzs = config.Vpc.MaxAzs
        });

        // ============================================================
        // 2. DynamoDB Tables (like modules/3.cosmos/*)
        // ============================================================
        var dynamoDbConstruct = new DynamoDbConstruct(this, "DynamoDb", new DynamoDbConstructProps
        {
            EventsTableName = config.DynamoDb.EventsTableName,
            OrleansClusterTableName = config.DynamoDb.OrleansClusterTableName,
            OrleansGrainStateTableName = config.DynamoDb.OrleansGrainStateTableName,
            OrleansRemindersTableName = config.DynamoDb.OrleansRemindersTableName,
            BillingMode = config.DynamoDb.BillingMode
        });

        // ============================================================
        // 3. SQS Queues (for Orleans Streaming)
        // ============================================================
        var sqsConstruct = new SqsConstruct(this, "Sqs", new SqsConstructProps
        {
            QueueNamePrefix = config.Sqs.QueueNamePrefix
        });

        // ============================================================
        // 4. S3 Bucket (for Snapshots, like modules/2.storages/*)
        // ============================================================
        var s3Construct = new S3Construct(this, "S3", new S3ConstructProps
        {
            SnapshotBucketName = config.S3.SnapshotBucketName
        });

        // ============================================================
        // 5. Secrets Manager (like modules/1.keyvault/*)
        // ============================================================
        var secretsConstruct = new SecretsManagerConstruct(this, "Secrets", new SecretsManagerConstructProps
        {
            SecretName = $"{config.ProjectName}-secrets-{config.Environment}"
        });

        // ============================================================
        // 6. Cloud Map (Service Discovery)
        // ============================================================
        var cloudMapConstruct = new CloudMapConstruct(this, "CloudMap", new CloudMapConstructProps
        {
            Vpc = vpcConstruct.Vpc,
            Namespace = $"{config.ProjectName}.internal"
        });

        // ============================================================
        // 7. ALB (like external ingress)
        // ============================================================
        var albConstruct = new AlbConstruct(this, "Alb", new AlbConstructProps
        {
            Vpc = vpcConstruct.Vpc,
            HttpPort = config.Orleans.HttpPort
        });

        // ============================================================
        // 8. ECS Fargate (like modules/12.backend-api/*)
        // ============================================================
        var ecsConstruct = new EcsFargateConstruct(this, "Ecs", new EcsFargateConstructProps
        {
            Vpc = vpcConstruct.Vpc,
            Cluster = config.Ecs.ClusterName,
            ServiceName = config.Ecs.ServiceName,
            DesiredCount = config.Ecs.DesiredCount,
            Cpu = config.Ecs.Cpu,
            Memory = config.Ecs.Memory,
            ContainerImage = config.ContainerImage,

            // Orleans ports
            SiloPort = config.Orleans.SiloPort,
            GatewayPort = config.Orleans.GatewayPort,
            HttpPort = config.Orleans.HttpPort,

            // Dependencies
            DynamoDbTables = dynamoDbConstruct.Tables,
            SqsQueues = sqsConstruct.Queues,
            S3Bucket = s3Construct.SnapshotBucket,
            Secrets = secretsConstruct.Secret,
            CloudMapService = cloudMapConstruct.Service,
            LoadBalancer = albConstruct.LoadBalancer,
            TargetGroup = albConstruct.TargetGroup
        });

        // ============================================================
        // Outputs
        // ============================================================
        new CfnOutput(this, "AlbDnsName", new CfnOutputProps
        {
            Value = albConstruct.LoadBalancer.LoadBalancerDnsName,
            Description = "ALB DNS Name"
        });

        new CfnOutput(this, "EcsClusterName", new CfnOutputProps
        {
            Value = ecsConstruct.Cluster.ClusterName,
            Description = "ECS Cluster Name"
        });
    }
}
```

### Constructs/EcsFargateConstruct.cs (ECS Module)
```csharp
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Constructs;

namespace AwsEcsFargate.Constructs;

public class EcsFargateConstruct : Construct
{
    public Cluster Cluster { get; }
    public FargateService Service { get; }

    public EcsFargateConstruct(Construct scope, string id, EcsFargateConstructProps props)
        : base(scope, id)
    {
        // Create ECS Cluster
        Cluster = new Cluster(this, "Cluster", new ClusterProps
        {
            Vpc = props.Vpc,
            ClusterName = props.Cluster,
            ContainerInsights = true
        });

        // Task Definition
        var taskDefinition = new FargateTaskDefinition(this, "TaskDef", new FargateTaskDefinitionProps
        {
            Cpu = props.Cpu,
            MemoryLimitMiB = props.Memory,
            RuntimePlatform = new RuntimePlatform
            {
                CpuArchitecture = CpuArchitecture.ARM64,  // Graviton for cost savings
                OperatingSystemFamily = OperatingSystemFamily.LINUX
            }
        });

        // Container Definition
        var container = taskDefinition.AddContainer("OrleansSilo", new ContainerDefinitionOptions
        {
            Image = ContainerImage.FromRegistry($"{props.ContainerImage.Repository}:{props.ContainerImage.Tag}"),
            Logging = LogDriver.AwsLogs(new AwsLogDriverProps
            {
                StreamPrefix = "orleans",
                LogRetention = RetentionDays.ONE_WEEK
            }),
            Environment = new Dictionary<string, string>
            {
                ["ORLEANS__CLUSTERID"] = props.OrleansClusterId,
                ["ORLEANS__SERVICEID"] = props.OrleansServiceId,
                ["ORLEANS__SILOPORT"] = props.SiloPort.ToString(),
                ["ORLEANS__GATEWAYPORT"] = props.GatewayPort.ToString(),
                ["AWS__REGION"] = Stack.Of(this).Region
            },
            PortMappings = new[]
            {
                new PortMapping { ContainerPort = props.HttpPort, Name = "http" },
                new PortMapping { ContainerPort = props.SiloPort, Name = "silo" },
                new PortMapping { ContainerPort = props.GatewayPort, Name = "gateway" }
            }
        });

        // Security Group for ECS Tasks
        var securityGroup = new SecurityGroup(this, "TaskSg", new SecurityGroupProps
        {
            Vpc = props.Vpc,
            Description = "Orleans Silo Security Group",
            AllowAllOutbound = true
        });

        // Allow ALB to reach HTTP port
        securityGroup.AddIngressRule(
            props.AlbSecurityGroup,
            Port.Tcp(props.HttpPort),
            "Allow ALB to HTTP port"
        );

        // Allow silo-to-silo communication (self-referencing)
        securityGroup.AddIngressRule(
            securityGroup,
            Port.Tcp(props.SiloPort),
            "Allow silo-to-silo communication"
        );

        securityGroup.AddIngressRule(
            securityGroup,
            Port.Tcp(props.GatewayPort),
            "Allow gateway communication"
        );

        // Fargate Service
        Service = new FargateService(this, "Service", new FargateServiceProps
        {
            Cluster = Cluster,
            TaskDefinition = taskDefinition,
            DesiredCount = props.DesiredCount,
            SecurityGroups = new[] { securityGroup },
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
            ServiceConnectConfiguration = new ServiceConnectProps
            {
                Namespace = props.CloudMapNamespace
            },
            CircuitBreaker = new DeploymentCircuitBreaker { Rollback = true }
        });

        // Register with ALB Target Group
        Service.AttachToApplicationTargetGroup(props.TargetGroup);

        // Auto Scaling
        var scaling = Service.AutoScaleTaskCount(new EnableScalingProps
        {
            MinCapacity = props.MinCapacity,
            MaxCapacity = props.MaxCapacity
        });

        scaling.ScaleOnCpuUtilization("CpuScaling", new CpuUtilizationScalingProps
        {
            TargetUtilizationPercent = 70
        });

        // Grant permissions to DynamoDB, SQS, S3
        foreach (var table in props.DynamoDbTables)
        {
            table.GrantReadWriteData(taskDefinition.TaskRole);
        }

        foreach (var queue in props.SqsQueues)
        {
            queue.GrantSendMessages(taskDefinition.TaskRole);
            queue.GrantConsumeMessages(taskDefinition.TaskRole);
        }

        props.S3Bucket.GrantReadWrite(taskDefinition.TaskRole);
        props.Secrets.GrantRead(taskDefinition.TaskRole);
    }
}
```

---

## Deployment Scripts

### scripts/deploy-all.sh
```bash
#!/bin/bash
# ============================================================================
# Complete Infrastructure Deployment (All Phases)
# ============================================================================
# Usage: ./deploy-all.sh <environment> [local-config-file]
# Example: ./deploy-all.sh dev ./dev.local.json
# ============================================================================

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(dirname "$SCRIPT_DIR")"

# Check arguments
if [ $# -lt 1 ]; then
    echo -e "${RED}Usage: $0 <environment> [local-config-file]${NC}"
    echo "  environment: dev, stg, or prod"
    echo "  local-config-file: Optional path to local.json config"
    echo ""
    echo "Example: $0 dev ./dev.local.json"
    exit 1
fi

ENVIRONMENT="$1"
LOCAL_CONFIG="${2:-$INFRA_DIR/${ENVIRONMENT}.local.json}"

if [ ! -f "$LOCAL_CONFIG" ]; then
    echo -e "${RED}Error: Config file not found: $LOCAL_CONFIG${NC}"
    echo "Create it from the sample: cp ${ENVIRONMENT}.local.json.sample ${ENVIRONMENT}.local.json"
    exit 1
fi

# Read configuration
AWS_ACCOUNT=$(jq -r '.account' "$LOCAL_CONFIG")
AWS_REGION=$(jq -r '.region' "$LOCAL_CONFIG")
PROJECT_NAME=$(jq -r '.projectName' "$LOCAL_CONFIG")

echo -e "${BOLD}${CYAN}============================================${NC}"
echo -e "${BOLD}${CYAN}AWS Infrastructure Deployment${NC}"
echo -e "${BOLD}${CYAN}============================================${NC}"
echo "Environment: ${ENVIRONMENT}"
echo "AWS Account: ${AWS_ACCOUNT}"
echo "AWS Region: ${AWS_REGION}"
echo "Project: ${PROJECT_NAME}"
echo ""

# Check AWS CLI login
echo -e "${YELLOW}Checking AWS CLI credentials...${NC}"
if ! aws sts get-caller-identity > /dev/null 2>&1; then
    echo -e "${RED}Error: Not authenticated with AWS.${NC}"
    echo "Run 'aws configure' or 'aws sso login' first."
    exit 1
fi
CALLER_IDENTITY=$(aws sts get-caller-identity --query 'Arn' --output text)
echo -e "${GREEN}Authenticated as: ${CALLER_IDENTITY}${NC}"
echo ""

# ============================================================================
# Phase 1: Bootstrap CDK (if needed)
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 1: CDK Bootstrap${NC}"

BOOTSTRAP_STACK="CDKToolkit"
STACK_EXISTS=$(aws cloudformation describe-stacks --stack-name "$BOOTSTRAP_STACK" --region "$AWS_REGION" 2>/dev/null || echo "")

if [ -z "$STACK_EXISTS" ]; then
    echo -e "${YELLOW}Bootstrapping CDK...${NC}"
    cd "$INFRA_DIR/src/AwsEcsFargate"
    cdk bootstrap "aws://${AWS_ACCOUNT}/${AWS_REGION}"
else
    echo -e "${GREEN}CDK already bootstrapped.${NC}"
fi
echo ""

# ============================================================================
# Phase 2: Build CDK App
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 2: Build CDK App${NC}"

cd "$INFRA_DIR/src/AwsEcsFargate"
dotnet build

echo ""

# ============================================================================
# Phase 3: Synthesize CloudFormation
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 3: Synthesize CloudFormation${NC}"

cdk synth -c environment="$ENVIRONMENT" -c config="$LOCAL_CONFIG"

echo ""

# ============================================================================
# Phase 4: Deploy Infrastructure
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 4: Deploy Infrastructure${NC}"

read -p "Deploy infrastructure? [Y/n] " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Nn]$ ]]; then
    cdk deploy -c environment="$ENVIRONMENT" -c config="$LOCAL_CONFIG" --require-approval broadening
fi

echo ""

# ============================================================================
# Phase 5: Build and Push Container Images
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 5: Build and Push Container Images${NC}"

read -p "Build and push container images? [Y/n] " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Nn]$ ]]; then
    "${SCRIPT_DIR}/build-push-images.sh" "$ENVIRONMENT" "$LOCAL_CONFIG"
fi

echo ""

# ============================================================================
# Phase 6: Update ECS Service
# ============================================================================
echo -e "${BOLD}${CYAN}Phase 6: Update ECS Service${NC}"

ECS_CLUSTER=$(jq -r '.ecs.clusterName' "$LOCAL_CONFIG")
ECS_SERVICE=$(jq -r '.ecs.serviceName' "$LOCAL_CONFIG")

read -p "Force new deployment of ECS service? [Y/n] " -n 1 -r
echo ""
if [[ ! $REPLY =~ ^[Nn]$ ]]; then
    aws ecs update-service \
        --cluster "$ECS_CLUSTER" \
        --service "$ECS_SERVICE" \
        --force-new-deployment \
        --region "$AWS_REGION"

    echo -e "${YELLOW}Waiting for deployment to stabilize...${NC}"
    aws ecs wait services-stable \
        --cluster "$ECS_CLUSTER" \
        --services "$ECS_SERVICE" \
        --region "$AWS_REGION"
fi

echo ""

# ============================================================================
# Completion
# ============================================================================
echo -e "${BOLD}${GREEN}============================================${NC}"
echo -e "${BOLD}${GREEN}Deployment Complete!${NC}"
echo -e "${BOLD}${GREEN}============================================${NC}"

# Get outputs
STACK_NAME="SekibanDcb-${ENVIRONMENT}"
ALB_DNS=$(aws cloudformation describe-stacks \
    --stack-name "$STACK_NAME" \
    --query "Stacks[0].Outputs[?OutputKey=='AlbDnsName'].OutputValue" \
    --output text \
    --region "$AWS_REGION" 2>/dev/null || echo "N/A")

echo ""
echo -e "${YELLOW}Deployed Resources:${NC}"
echo "  Stack: ${STACK_NAME}"
echo "  ALB DNS: ${ALB_DNS}"
echo "  API Endpoint: http://${ALB_DNS}"
echo ""
```

### scripts/build-push-images.sh
```bash
#!/bin/bash
# ============================================================================
# Build and Push Container Images to ECR
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INFRA_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_ROOT="$(dirname "$(dirname "$(dirname "$INFRA_DIR")")")"

ENVIRONMENT="${1:-dev}"
LOCAL_CONFIG="${2:-$INFRA_DIR/${ENVIRONMENT}.local.json}"

AWS_ACCOUNT=$(jq -r '.account' "$LOCAL_CONFIG")
AWS_REGION=$(jq -r '.region' "$LOCAL_CONFIG")
REPOSITORY=$(jq -r '.containerImage.repository' "$LOCAL_CONFIG")
TAG=$(jq -r '.containerImage.tag // "latest"' "$LOCAL_CONFIG")

ECR_URI="${AWS_ACCOUNT}.dkr.ecr.${AWS_REGION}.amazonaws.com"

echo "Building and pushing container image..."
echo "  Repository: ${ECR_URI}/${REPOSITORY}:${TAG}"

# Login to ECR
aws ecr get-login-password --region "$AWS_REGION" | \
    docker login --username AWS --password-stdin "$ECR_URI"

# Create repository if not exists
aws ecr describe-repositories --repository-names "$REPOSITORY" --region "$AWS_REGION" 2>/dev/null || \
    aws ecr create-repository --repository-name "$REPOSITORY" --region "$AWS_REGION"

# Build image (ARM64 for Graviton)
docker buildx build \
    --platform linux/arm64 \
    --tag "${ECR_URI}/${REPOSITORY}:${TAG}" \
    --push \
    -f "${PROJECT_ROOT}/Dockerfile" \
    "$PROJECT_ROOT"

echo "Image pushed: ${ECR_URI}/${REPOSITORY}:${TAG}"
```

---

## GitHub Actions Workflow

### .github/workflows/aws-deploy-dev.yml
```yaml
name: Deploy to AWS (Dev)

on:
  push:
    branches: [develop]
    paths:
      - 'dcb/src/**'
      - 'Infrastructure/AwsEcsFargate/**'
  workflow_dispatch:

env:
  AWS_REGION: ap-northeast-1
  ENVIRONMENT: dev

jobs:
  deploy:
    runs-on: ubuntu-latest
    permissions:
      id-token: write  # Required for OIDC
      contents: read

    steps:
      - uses: actions/checkout@v4

      - name: Configure AWS credentials (OIDC)
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: ${{ secrets.AWS_DEPLOY_ROLE_ARN }}
          aws-region: ${{ env.AWS_REGION }}

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Setup Node.js (for CDK)
        uses: actions/setup-node@v4
        with:
          node-version: '20'

      - name: Install AWS CDK
        run: npm install -g aws-cdk

      - name: Build CDK App
        working-directory: Infrastructure/AwsEcsFargate/src/AwsEcsFargate
        run: dotnet build

      - name: CDK Diff
        working-directory: Infrastructure/AwsEcsFargate
        run: |
          cdk diff -c environment=${{ env.ENVIRONMENT }}

      - name: CDK Deploy
        working-directory: Infrastructure/AwsEcsFargate
        run: |
          cdk deploy -c environment=${{ env.ENVIRONMENT }} \
            --require-approval never \
            --ci

      - name: Login to Amazon ECR
        id: login-ecr
        uses: aws-actions/amazon-ecr-login@v2

      - name: Build and Push Container Image
        env:
          ECR_REGISTRY: ${{ steps.login-ecr.outputs.registry }}
          IMAGE_TAG: ${{ github.sha }}
        run: |
          docker buildx build \
            --platform linux/arm64 \
            --tag $ECR_REGISTRY/sekiban-api:$IMAGE_TAG \
            --tag $ECR_REGISTRY/sekiban-api:latest \
            --push \
            -f Dockerfile .

      - name: Update ECS Service
        run: |
          aws ecs update-service \
            --cluster sekiban-cluster \
            --service orleans-silo \
            --force-new-deployment

      - name: Wait for Deployment
        run: |
          aws ecs wait services-stable \
            --cluster sekiban-cluster \
            --services orleans-silo
```

---

## Terraform Alternative

If Terraform is preferred over CDK, here's the equivalent structure:

### Directory Structure (Terraform)
```
Infrastructure/
├── AwsEcsFargate-terraform/
│   ├── modules/
│   │   ├── vpc/
│   │   │   ├── main.tf
│   │   │   ├── variables.tf
│   │   │   └── outputs.tf
│   │   ├── dynamodb/
│   │   ├── sqs/
│   │   ├── s3/
│   │   ├── ecs/
│   │   ├── alb/
│   │   └── cloudmap/
│   │
│   ├── environments/
│   │   ├── dev/
│   │   │   ├── main.tf
│   │   │   ├── terraform.tfvars
│   │   │   └── backend.tf
│   │   ├── stg/
│   │   └── prod/
│   │
│   ├── scripts/
│   │   ├── deploy.sh
│   │   └── destroy.sh
│   │
│   └── README.md
```

### environments/dev/main.tf
```hcl
terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket         = "sekiban-terraform-state"
    key            = "dev/terraform.tfstate"
    region         = "ap-northeast-1"
    dynamodb_table = "terraform-locks"
    encrypt        = true
  }
}

provider "aws" {
  region = var.aws_region
}

module "vpc" {
  source = "../../modules/vpc"

  environment = var.environment
  cidr_block  = var.vpc_cidr
  max_azs     = var.vpc_max_azs
}

module "dynamodb" {
  source = "../../modules/dynamodb"

  environment        = var.environment
  events_table_name  = var.dynamodb_events_table
  billing_mode       = var.dynamodb_billing_mode
}

module "sqs" {
  source = "../../modules/sqs"

  environment       = var.environment
  queue_name_prefix = var.sqs_queue_prefix
}

module "s3" {
  source = "../../modules/s3"

  environment  = var.environment
  bucket_name  = var.s3_bucket_name
}

module "ecs" {
  source = "../../modules/ecs"

  environment     = var.environment
  vpc_id          = module.vpc.vpc_id
  private_subnets = module.vpc.private_subnet_ids

  cluster_name  = var.ecs_cluster_name
  service_name  = var.ecs_service_name
  desired_count = var.ecs_desired_count
  cpu           = var.ecs_cpu
  memory        = var.ecs_memory

  container_image = var.container_image

  silo_port    = var.orleans_silo_port
  gateway_port = var.orleans_gateway_port
  http_port    = var.orleans_http_port

  dynamodb_table_arns = module.dynamodb.table_arns
  sqs_queue_arns      = module.sqs.queue_arns
  s3_bucket_arn       = module.s3.bucket_arn
}

module "alb" {
  source = "../../modules/alb"

  environment     = var.environment
  vpc_id          = module.vpc.vpc_id
  public_subnets  = module.vpc.public_subnet_ids

  target_group_port = var.orleans_http_port
  ecs_service_sg    = module.ecs.service_security_group_id
}
```

---

## Comparison: Azure vs AWS Structure

| Azure (Bicep) | AWS (CDK) | AWS (Terraform) |
|---------------|-----------|-----------------|
| `main.bicep` | `AwsEcsFargateStack.cs` | `main.tf` |
| `modules/*.bicep` | `Constructs/*.cs` | `modules/*/` |
| `parameters/*.json` | `parameters/*.json` | `*.tfvars` |
| `scripts/*.sh` | `scripts/*.sh` | `scripts/*.sh` |
| `*.local.json` | `*.local.json` | `terraform.tfvars` |
| `az deployment` | `cdk deploy` | `terraform apply` |

---

## Recommendation

### For this project: **AWS CDK (C#)**

1. **Consistency**: Same language as application code
2. **Type Safety**: Catch errors at compile time
3. **IDE Support**: Full IntelliSense in Rider/VS
4. **Familiar Patterns**: Similar to Bicep's modular approach
5. **State Management**: Uses CloudFormation (no separate state file)

### Implementation Priority

1. Create `Infrastructure/AwsEcsFargate/` directory structure
2. Implement core CDK constructs (VPC, DynamoDB, SQS, S3)
3. Implement ECS Fargate construct with Orleans ports
4. Add deployment scripts
5. Set up GitHub Actions workflow

---

## References

- [AWS CDK (C#) Developer Guide](https://docs.aws.amazon.com/cdk/v2/guide/work-with-cdk-csharp.html)
- [AWS CDK ECS Patterns](https://docs.aws.amazon.com/cdk/api/v2/docs/aws-cdk-lib.aws_ecs_patterns-readme.html)
- [Terraform AWS Provider](https://registry.terraform.io/providers/hashicorp/aws/latest)
- [GitHub Actions AWS Deployment](https://docs.github.com/en/actions/deployment/deploying-to-your-cloud-provider/deploying-to-amazon-elastic-container-service)
