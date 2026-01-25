#!/bin/bash

# Deploy WebNext (Next.js) container to Azure Container Apps
# parameter $1 is the name of the environment

set -e

if [ -z "$1" ]; then
    echo "Usage: $0 <environment-name>"
    exit 1
fi

ARG_INPUT="$1"
if [[ "$ARG_INPUT" == *.local.json ]]; then
    BASENAME="${ARG_INPUT##*/}"
    ENVIRONMENT="${BASENAME%.local.json}"
    CONFIG_FILE="$BASENAME"
    CONFIG_PATH="$ARG_INPUT"
else
    ENVIRONMENT="$ARG_INPUT"
    CONFIG_FILE="${ENVIRONMENT}.local.json"
    CONFIG_PATH="$CONFIG_FILE"
fi

# Check if config file exists
if [ ! -f "$CONFIG_PATH" ]; then
    echo "Error: Configuration file $CONFIG_PATH not found"
    exit 1
fi

# get resource group name from {environment}.local.json parameter name is "resourceGroupName"
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_PATH")

# Get WebNext relative path from config file (fallback to default if not specified)
WEBNEXT_PATH=$(jq -r '.webnextRelativePath // "../../SekibanDcbDecider.WebNext"' "$CONFIG_PATH")

# Verify the WebNext path exists
if [ ! -d "$WEBNEXT_PATH" ]; then
    echo "Error: WebNext directory not found at $WEBNEXT_PATH"
    exit 1
fi

echo "Resource Group: $RESOURCE_GROUP"
echo "WebNext path: $WEBNEXT_PATH"

# Container App name and ACR name will follow the naming convention
CONTAINER_APP_NAME="webnext-${RESOURCE_GROUP}"
ACR_NAME=$(echo "acr-${RESOURCE_GROUP}" | tr '[:upper:]' '[:lower:]' | tr -d '-')

echo "Container App Name: $CONTAINER_APP_NAME"
echo "ACR Name: $ACR_NAME"

# Get ACR credentials
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query "username" -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query "passwords[0].value" -o tsv)
ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer -o tsv)

if [ -z "$ACR_LOGIN_SERVER" ]; then
    echo "Error: Failed to get ACR login server. Make sure ACR exists."
    exit 1
fi

echo "ACR Login Server: $ACR_LOGIN_SERVER"

# Login Docker
echo "Logging into ACR..."
echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" -u "$ACR_USERNAME" --password-stdin

# Build and push Docker image with platform specification
echo "Building Docker image for linux/amd64 platform..."

# Navigate to project root (two levels up from infrastructure/azure_container_apps)
pushd "../../" > /dev/null

# The Dockerfile path should be relative to project root
DOCKERFILE_PATH="${WEBNEXT_PATH#../../}/Dockerfile"
echo "Dockerfile path: $DOCKERFILE_PATH"

docker buildx build --platform linux/amd64 \
    -t "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest" \
    -f "$DOCKERFILE_PATH" \
    --push .

BUILD_RESULT=$?
popd > /dev/null

# Check if building and pushing image was successful
if [ $BUILD_RESULT -eq 0 ]; then
    echo "Building and pushing Docker image completed successfully."
else
    echo "Error: Failed to build and push Docker image"
    exit 1
fi

# Update the container app with the new image (or show message if first deployment)
echo "Updating WebNext container in Azure Container Apps..."
if az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    az containerapp update \
        --name "$CONTAINER_APP_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --image "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest"

    if [ $? -eq 0 ]; then
        echo "WebNext container updated successfully."

        # Get the container app URL
        FQDN=$(az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" --query "properties.configuration.ingress.fqdn" -o tsv)
        echo ""
        echo "WebNext URL: https://$FQDN"
    else
        echo "Error: Failed to update WebNext container"
        exit 1
    fi
else
    echo "Container app '$CONTAINER_APP_NAME' does not exist yet."
    echo "Please run './runbicep.sh $1 aca_main.bicep' first to create the container apps."
    echo "The Docker image has been pushed successfully and is ready for deployment."
fi

echo ""
echo "Deployment process completed."
