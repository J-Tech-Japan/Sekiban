# Deployment Guide - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Dapr Setup](11_dapr_setup.md)
> - [Unit Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md) (You are here)

## Deployment Guide

This guide covers deployment options for Sekiban applications using the provided Bicep templates. Currently, Azure
deployment is supported with templates for both Orleans and Dapr implementations.

## Prerequisites

### Azure CLI Setup

1. **Login to Azure**: First, login to your target Azure tenant:

```bash
# Login by specifying tenant ID
az login --tenant <tenant-id>

# Or login using organization domain name
az login --tenant contoso.onmicrosoft.com

# When multiple accounts exist with the same username, use device code
az login --tenant <tenant-id> --use-device-code
```

2. **Register Required Resource Providers**: Each deployment option requires specific Azure resource providers.

## Deployment Options

Sekiban provides pre-configured Bicep templates for different Azure deployment scenarios:

### Orleans-based Deployments

#### 1. Azure App Service (Full Featured)

**Location**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice/`

**Best for**: Production applications requiring full App Service features, SSL certificates, custom domains, and
integrated monitoring.

**Features**:

- Azure App Service with scaling capabilities
- Azure SQL Database or Cosmos DB
- Application Insights integration
- SSL certificates and custom domains
- Staging slots for blue-green deployments

#### 2. Azure App Service (Minimal)

**Location**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice_minimal/`

**Best for**: Development, testing, or cost-optimized production deployments.

**Features**:

- Basic Azure App Service
- Minimal resource configuration
- Essential monitoring
- Cost-optimized setup

#### 3. Azure Container Apps (Orleans)

**Location**: `templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_container_apps/`

**Best for**: Cloud-native applications requiring containerized deployment with advanced scaling and microservices
architecture.

**Features**:

- Container Apps Environment
- Auto-scaling based on demand
- KEDA-based scaling triggers
- Integrated Log Analytics
- Service discovery

### Dapr-based Deployments

#### 4. Azure Container Apps (Dapr)

**Location**: `templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/infrastructure/azure_container_apps/`

**Best for**: Microservices applications leveraging Dapr's sidecar pattern with cloud-native scaling.

**Features**:

- Dapr integration with sidecar pattern
- Service Bus for pub/sub messaging
- Container Apps with Dapr components
- State management with external stores
- Distributed tracing and monitoring

## Deployment Steps

### Step 1: Choose Your Template

Navigate to the appropriate template directory based on your architecture choice:

```bash
# For Orleans App Service
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice/

# For Orleans Container Apps
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_container_apps/

# For Orleans Minimal App Service
cd templates/Sekiban.Pure.Templates/content/Sekiban.Orleans.Aspire/infrastructure/azure_appservice_minimal/

# For Dapr Container Apps
cd templates/Sekiban.Pure.Templates/content/Sekiban.Dapr.Aspire/infrastructure/azure_container_apps/
```

### Step 2: Configure Deployment Settings

Each template directory contains a `how.md` file with specific instructions. Generally, you'll need to:

1. **Create Configuration File**: Create a deployment configuration file (typically `mydeploy.local.json`):

```json
{
    "resourceGroupName": "your-sekiban-app-123",
    "location": "japaneast",
    "backendRelativePath": "../../YourProject.ApiService",
    "frontendRelativePath": "../../YourProject.Web",
    "logincommand": "az login --tenant yourorg.onmicrosoft.com --use-device-code"
}
```

**Important**: Use only lowercase letters, numbers, and hyphens in resource group names.

2. **Register Resource Providers**: Run the provider registration script:

```bash
chmod +x ./register_providers.sh
./register_providers.sh
```

### Step 3: Deploy Infrastructure

Follow the specific deployment steps in each template's `how.md` file. Typically involves:

```bash
# Make deployment script executable
chmod +x ./deploy.sh

# Run deployment with your configuration
./deploy.sh mydeploy
```

## Template-Specific Features

### Orleans App Service Template

**Resource Providers Required**:

- `Microsoft.Web` (App Service)
- `Microsoft.Sql` (Azure SQL Database)
- `Microsoft.DocumentDB` (Cosmos DB)
- `Microsoft.Insights` (Application Insights)

**Key Components**:

- App Service Plan with auto-scaling
- Azure SQL Database or Cosmos DB
- Application Insights for monitoring
- Key Vault for secrets management

### Orleans Container Apps Template

**Resource Providers Required**:

- `Microsoft.App` (Container Apps)
- `Microsoft.ContainerService` (Container Apps Environment)
- `Microsoft.OperationalInsights` (Log Analytics)
- `Microsoft.DocumentDB` (Cosmos DB)

**Key Components**:

- Container Apps Environment
- Container Apps with Orleans configuration
- Log Analytics workspace
- Application Insights
- Cosmos DB or PostgreSQL

### Dapr Container Apps Template

**Resource Providers Required**:

- `Microsoft.App` (Container Apps)
- `Microsoft.ContainerService` (Container Apps Environment)
- `Microsoft.OperationalInsights` (Log Analytics)
- `Microsoft.ServiceBus` (Service Bus for Dapr pub/sub)

**Key Components**:

- Container Apps Environment with Dapr
- Dapr components (state store, pub/sub)
- Service Bus for messaging
- Redis Cache for state management
- Log Analytics workspace

## Environment Configuration

### Development vs Production

Templates support environment-specific configurations:

```json
{
    "environment": "development",
    "resourceGroupName": "sekiban-dev-123",
    "scaling": {
        "minReplicas": 1,
        "maxReplicas": 3
    }
}
```

```json
{
    "environment": "production", 
    "resourceGroupName": "sekiban-prod-456",
    "scaling": {
        "minReplicas": 3,
        "maxReplicas": 30
    }
}
```

### Database Configuration

Templates support multiple database options:

**Azure SQL Database** (Orleans):

```json
{
    "database": {
        "type": "sql",
        "sku": "S2",
        "backupRetention": 7
    }
}
```

**Cosmos DB** (Orleans/Dapr):

```json
{
    "database": {
        "type": "cosmos",
        "consistencyLevel": "Session",
        "throughput": 400
    }
}
```

**PostgreSQL** (Dapr):

```json
{
    "database": {
        "type": "postgresql",
        "sku": "B_Gen5_1",
        "storage": "5120"
    }
}
```

## Monitoring and Observability

All templates include monitoring capabilities:

### Application Insights

- Request tracking
- Dependency monitoring
- Exception logging
- Custom metrics

### Log Analytics

- Container logs
- Application logs
- Performance metrics
- Query capabilities

### Health Checks

- Readiness probes
- Liveness probes
- Startup probes

## Security Considerations

### Key Vault Integration

Templates include Azure Key Vault for secure secret management:

```json
{
    "keyVault": {
        "name": "sekiban-kv-123",
        "secrets": [
            "ConnectionStrings--DefaultConnection",
            "ApplicationInsights--InstrumentationKey"
        ]
    }
}
```

### Managed Identity

Templates use Managed Identity for secure Azure service authentication:

```bicep
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
    name: 'sekiban-identity'
    location: location
}
```

### Network Security

- Private endpoints for databases
- Virtual network integration
- Network security groups
- Application Gateway for SSL termination

## Scaling Configuration

### Orleans Scaling

```json
{
    "orleans": {
        "clustering": {
            "providerId": "AzureTable",
            "deploymentId": "sekiban-prod"
        },
        "silos": {
            "min": 2,
            "max": 10
        }
    }
}
```

### Dapr Scaling

```json
{
    "dapr": {
        "components": {
            "stateStore": "redis",
            "pubSub": "servicebus"
        },
        "scaling": {
            "triggers": ["cpu", "memory", "queue-length"]
        }
    }
}
```

## CI/CD Integration

Templates are designed to work with Azure DevOps and GitHub Actions:

### Azure DevOps Pipeline

```yaml
- task: AzureCLI@2
  displayName: 'Deploy Infrastructure'
  inputs:
    azureSubscription: '$(azureSubscription)'
    scriptType: 'bash'
    scriptLocation: 'scriptPath'
    scriptPath: './infrastructure/deploy.sh'
    arguments: '$(deploymentConfig)'
```

### GitHub Actions

```yaml
- name: Deploy to Azure
  uses: azure/CLI@v1
  with:
    azcliversion: 2.50.0
    inlineScript: |
      cd ./infrastructure/azure_container_apps
      chmod +x ./deploy.sh
      ./deploy.sh ${{ secrets.DEPLOYMENT_CONFIG }}
```

## Troubleshooting

### Common Issues

1. **Resource Provider Not Registered**:
   ```bash
   az provider register --namespace Microsoft.App
   ```

2. **Insufficient Permissions**:
    - Ensure you have Contributor role on the subscription
    - Check resource group permissions

3. **Template Validation Errors**:
   ```bash
   az deployment group validate --resource-group myRG --template-file main.bicep
   ```

4. **Container Image Issues**:
    - Verify container registry access
    - Check image tags and versions

### Debugging Deployments

```bash
# Check deployment status
az deployment group show --resource-group myRG --name myDeployment

# View deployment logs
az monitor activity-log list --resource-group myRG

# Check container logs
az containerapp logs show --name myapp --resource-group myRG
```

## Future Deployment Options

The following deployment options are planned for future releases:

### On-Premises Deployment

- Docker Compose templates
- Kubernetes manifests
- Helm charts

### AWS Deployment

- CloudFormation templates
- ECS/Fargate deployment
- EKS cluster setup
- Lambda serverless options

### Google Cloud Platform

- Cloud Run deployment
- GKE cluster setup
- Cloud Functions integration

## Best Practices

1. **Environment Separation**: Use separate resource groups for different environments
2. **Resource Naming**: Follow consistent naming conventions
3. **Cost Management**: Use appropriate SKUs for each environment
4. **Security**: Always use Managed Identity and Key Vault
5. **Monitoring**: Enable comprehensive logging and monitoring
6. **Backup**: Configure appropriate backup strategies
7. **Scaling**: Configure auto-scaling based on actual usage patterns

## Next Steps

After deployment:

1. Configure monitoring dashboards
2. Set up alerts and notifications
3. Implement backup and disaster recovery
4. Plan capacity scaling
5. Set up CI/CD pipelines
6. Configure security policies

For specific deployment instructions, always refer to the `how.md` file in your chosen template directory.
