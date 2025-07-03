#!/bin/bash

# parameter $1 is the name of the environment

if [ -z "$1" ]; then
    echo "Usage: $0 <environment-name>"
    exit 1
fi

ENVIRONMENT=$1
CONFIG_FILE="${ENVIRONMENT}.local.json"

# Check if config file exists
if [ ! -f "$CONFIG_FILE" ]; then
    echo "Error: Configuration file $CONFIG_FILE not found"
    exit 1
fi

# get resource group name from {environment}.local.json parameter name is "resourceGroupName"
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_FILE")

# Get backend relative path from config file
BACKEND_PATH=$(jq -r '.backendRelativePath' "$CONFIG_FILE")

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

echo "Deployment process completed."