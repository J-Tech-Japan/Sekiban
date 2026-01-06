#!/bin/bash

# parameter $1 is the name of the environment

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

# Get backend relative path from config file
BACKEND_PATH=$(jq -r '.backendRelativePath' "$CONFIG_PATH")

# Verify the backend path exists
if [ ! -d "$BACKEND_PATH" ]; then
    echo "Error: Backend directory not found at $BACKEND_PATH"
    exit 1
fi

echo "Backend path: $BACKEND_PATH"

# Container App name and ACR name will follow the naming convention
CONTAINER_APP_NAME="backend-${RESOURCE_GROUP}"
ACR_NAME=$(echo "acr-${RESOURCE_GROUP}" | tr '[:upper:]' '[:lower:]' | tr -d '-')
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query "username" -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query "passwords[0].value" -o tsv)
ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer -o tsv)

# Login to Docker
echo "Logging into ACR..."
echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" -u "$ACR_USERNAME" --password-stdin

# Build and push Docker image with platform specification
echo "Building Docker image for linux/amd64 platform..."
pushd "../../"
docker buildx build --platform linux/amd64 -t "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest" -f "${BACKEND_PATH#../../}/Dockerfile" --push .

# Check if building and pushing image was successful (buildx pushes automatically)
if [ $? -eq 0 ]; then
    echo "Building and pushing Docker image completed successfully."
else
    echo "Error: Failed to build and push Docker image"
    exit 1
fi

# Update the container app with the new image (or show message if first deployment)
echo "Updating backend container in Azure Container Apps..."
if az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    az containerapp update --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" --image "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest"
    if [ $? -eq 0 ]; then
        echo "Backend container updated successfully."
    else
        echo "Error: Failed to update backend container"
        exit 1
    fi
else
    echo "Container app '$CONTAINER_APP_NAME' does not exist yet."
    echo "Please run './runbicep.sh $1 aca_apps.bicep' first to create the container apps."
    echo "The Docker image has been pushed successfully and is ready for deployment."
fi

echo "Deployment process completed."