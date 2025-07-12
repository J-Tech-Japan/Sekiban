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

# Get Frontend relative path from config file
FRONTEND_PATH=$(jq -r '.frontendRelativePath' "$CONFIG_FILE")

# Verify the Frontend path exists
if [ ! -d "$FRONTEND_PATH" ]; then
    echo "Error: Frontend directory not found at $FRONTEND_PATH"
    exit 1
fi

echo "Frontend path: $FRONTEND_PATH"

# Container App name and ACR name will follow the naming convention
CONTAINER_APP_NAME="frontend-${RESOURCE_GROUP}"
ACR_NAME=$(echo "acr-${RESOURCE_GROUP}" | tr '[:upper:]' '[:lower:]' | tr -d '-')
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query "username" -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query "passwords[0].value" -o tsv)
ACR_LOGIN_SERVER=$(az acr show --name $ACR_NAME --query loginServer -o tsv)

# Login Docker
echo "Logging into ACR..."
echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" -u "$ACR_USERNAME" --password-stdin

# Build and push Docker image
echo "Building Docker image..."
pushd "../../"
docker build -t "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest" -f "${FRONTEND_PATH#../../}/Dockerfile" .

# Check if building was successful
if [ $? -eq 0 ]; then
    echo "Building Docker image completed successfully."
else
    echo "Error: Failed to build Docker image"
    exit 1
fi

echo "Pushing Docker image: $CONTAINER_APP_NAME..."
docker push "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest"

# Check if deployment was successful
if [ $? -eq 0 ]; then
    echo "Pushing Docker image to ACR completed successfully."
else
    echo "Error: Failed to push Docker image to ACR"
    exit 1
fi

echo "Deployment process completed."