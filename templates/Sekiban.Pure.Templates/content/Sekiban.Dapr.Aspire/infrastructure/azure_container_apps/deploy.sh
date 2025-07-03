#!/bin/bash

# Deployment script for Sekiban Dapr Aspire on Azure Container Apps

set -e

# Check if environment name is provided
if [ -z "$1" ]; then
    echo "Usage: $0 <environment-name>"
    echo "Example: $0 dev"
    exit 1
fi

ENVIRONMENT=$1
CONFIG_FILE="${ENVIRONMENT}.local.json"

# Check if config file exists
if [ ! -f "$CONFIG_FILE" ]; then
    echo "Error: Configuration file $CONFIG_FILE not found"
    echo "Please create a configuration file with the following structure:"
    echo '{
  "resourceGroupName": "your-resource-group-name",
  "location": "japaneast"
}'
    exit 1
fi

# Get configuration values
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_FILE")
LOCATION=$(jq -r '.location' "$CONFIG_FILE")

echo "================================"
echo "Deploying Sekiban Dapr Aspire"
echo "Environment: $ENVIRONMENT"
echo "Resource Group: $RESOURCE_GROUP"
echo "Location: $LOCATION"
echo "================================"

# Step 1: Create Resource Group
echo "Step 1: Creating Resource Group..."
./create_resource_group.sh $ENVIRONMENT

# Step 2: Create Log Analytics Workspace
echo "Step 2: Creating Log Analytics Workspace..."
./create_log-analytics.sh $ENVIRONMENT

# Step 3: Create Container Registry
echo "Step 3: Creating Container Registry..."
./create_container_registry.sh $ENVIRONMENT

# Step 4: Deploy infrastructure with Bicep
echo "Step 4: Deploying infrastructure..."
SHARED_KEY=$(az monitor log-analytics workspace get-shared-keys \
  --resource-group $RESOURCE_GROUP \
  --workspace-name law-$RESOURCE_GROUP \
  --query primarySharedKey -o tsv)

az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "aca_main.bicep" \
  --parameters "logAnalyticsSharedKey=$SHARED_KEY" \
  --parameters "daprStateStoreType=azureblobstorage" \
  --parameters "daprPubSubType=azurestoragequeues"

# Step 5: Grant user access to Key Vault
echo "Step 5: Granting user access to Key Vault..."
./user_access_keyvault.sh $ENVIRONMENT

# Step 6: Deploy backend application
echo "Step 6: Deploying backend application..."
echo "Please build and push your backend container image first:"
echo "  cd ../../../DaprSekiban.ApiService"
echo "  dotnet publish -c Release"
echo "  docker build -t backend-$RESOURCE_GROUP ."
echo "  docker tag backend-$RESOURCE_GROUP ${acrName}.azurecr.io/backend-$RESOURCE_GROUP:latest"
echo "  az acr login --name ${acrName}"
echo "  docker push ${acrName}.azurecr.io/backend-$RESOURCE_GROUP:latest"
echo ""
echo "Then run: ./code_deploy_backend.sh $ENVIRONMENT"

# Step 7: Deploy frontend application
echo "Step 7: Deploying frontend application..."
echo "Please build and push your frontend container image first:"
echo "  cd ../../../DaprSekiban.Web"
echo "  dotnet publish -c Release"
echo "  docker build -t blazor-$RESOURCE_GROUP ."
echo "  docker tag blazor-$RESOURCE_GROUP ${acrName}.azurecr.io/blazor-$RESOURCE_GROUP:latest"
echo "  az acr login --name ${acrName}"
echo "  docker push ${acrName}.azurecr.io/blazor-$RESOURCE_GROUP:latest"
echo ""
echo "Then run: ./code_deploy_frontend.sh $ENVIRONMENT"

echo "================================"
echo "Deployment completed!"
echo "================================"
echo ""
echo "Next steps:"
echo "1. Build and push your container images"
echo "2. Run the code deployment scripts"
echo "3. Access your application at the provided URLs"