# Sekiban Dapr Aspire - Azure Container Apps Deployment

This directory contains the infrastructure as code (IaC) scripts to deploy Sekiban Dapr Aspire to Azure Container Apps.

## Architecture Overview

The deployment creates the following Azure resources:

- **Azure Container Apps Environment** - Managed environment with Dapr enabled
- **Azure Cosmos DB** - Event store for Sekiban
- **Azure Storage Account** - Used for Dapr state store and pub/sub
- **Azure Key Vault** - Secure storage for connection strings and secrets
- **Azure Container Registry** - Container image storage
- **Azure Application Insights** - Application monitoring
- **Azure Virtual Network** - Network isolation for container apps

## Dapr Components

The deployment configures the following Dapr components:

1. **State Store** - Azure Blob Storage or Table Storage
2. **Pub/Sub** - Azure Storage Queues (binding component)

## Prerequisites

- Azure CLI installed and logged in
- PowerShell or Bash shell
- jq (for Bash scripts)
- Docker for building container images

## Configuration

Create a configuration file named `{environment}.local.json` (e.g., `dev.local.json`):

```json
{
  "resourceGroupName": "your-resource-group-name",
  "location": "japaneast"
}
```

## Deployment Steps

### Using Bash

```bash
# Make scripts executable
chmod +x *.sh

# Run the deployment
./deploy.sh dev
```

### Using PowerShell

```powershell
# Run the deployment
.\deploy.ps1 -Environment dev
```

## Manual Deployment Steps

If you prefer to deploy step by step:

1. **Create Resource Group**
   ```bash
   ./create_resource_group.sh dev
   ```

2. **Create Log Analytics Workspace**
   ```bash
   ./create_log-analytics.sh dev
   ```

3. **Create Container Registry**
   ```bash
   ./create_container_registry.sh dev
   ```

4. **Deploy Infrastructure**
   ```bash
   ./runbicep.sh dev aca_main.bicep
   ```

5. **Grant Key Vault Access**
   ```bash
   ./user_access_keyvault.sh dev
   ```

## Building and Deploying Applications

### Backend API Service

```bash
cd ../../../DaprSekiban.ApiService
dotnet publish -c Release
docker build -t backend-{resource-group} .
docker tag backend-{resource-group} {acr-name}.azurecr.io/backend-{resource-group}:latest
az acr login --name {acr-name}
docker push {acr-name}.azurecr.io/backend-{resource-group}:latest

# Deploy to Container Apps
cd infrastructure/azure_container_apps
./code_deploy_backend.sh dev
```

### Frontend Blazor App

```bash
cd ../../../DaprSekiban.Web
dotnet publish -c Release
docker build -t blazor-{resource-group} .
docker tag blazor-{resource-group} {acr-name}.azurecr.io/blazor-{resource-group}:latest
az acr login --name {acr-name}
docker push {acr-name}.azurecr.io/blazor-{resource-group}:latest

# Deploy to Container Apps
cd infrastructure/azure_container_apps
./code_deploy_frontend.sh dev
```

## Customization

### Dapr State Store Type

You can choose between Azure Blob Storage or Table Storage:

```bash
az deployment group create \
  --resource-group {resource-group} \
  --template-file aca_main.bicep \
  --parameters daprStateStoreType=azureblobstorage  # or azuretablestorage
```

### Dapr Pub/Sub Type

Currently supports Azure Storage Queues (as binding). For true pub/sub, Azure Service Bus would need to be implemented.

## Troubleshooting

1. **Key Vault Access Issues**
   - Run `./user_access_keyvault.sh {environment}` to grant yourself access
   - Check that the Container App managed identity has access to Key Vault

2. **Dapr Component Issues**
   - Check Dapr component status in Azure Portal
   - Review container logs for Dapr sidecar initialization
   - Ensure storage account keys are correctly stored in Key Vault

3. **Container Deployment Issues**
   - Verify container images are pushed to ACR
   - Check Container App revision status
   - Review application logs in Application Insights

## Clean Up

To remove all resources:

```bash
az group delete --name {resource-group} --yes
```

To purge Key Vault (if soft-delete is enabled):

```bash
./purge_keyvault.sh {environment}
```