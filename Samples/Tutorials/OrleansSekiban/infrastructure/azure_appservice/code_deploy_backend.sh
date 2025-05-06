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
KEYVAULT_NAME="kv-${RESOURCE_GROUP}"
# get location name from {environment}.local.json parameter name is "location"
LOCATION=$(jq -r '.location' "$CONFIG_FILE")

# Get backend relative path from config file
BACKEND_PATH=$(jq -r '.backendRelativePath' "$CONFIG_FILE")

# Verify the backend path exists
if [ ! -d "$BACKEND_PATH" ]; then
    echo "Error: Backend directory not found at $BACKEND_PATH"
    exit 1
fi

echo "Backend path: $BACKEND_PATH"

# App Service name will follow the naming convention
APP_SERVICE_NAME="backend-${RESOURCE_GROUP}"

# Build and publish the application
echo "Building and publishing .NET 9.0 application..."
pushd "$BACKEND_PATH"

# Create a temporary publish directory
PUBLISH_DIR="publish"
mkdir -p "$PUBLISH_DIR"

# Build and publish the .NET 9.0 application
dotnet publish -c Release -o "$PUBLISH_DIR" --self-contained false

# Check if publish was successful
if [ $? -ne 0 ]; then
    echo "Error: Failed to build and publish the application"
    popd
    exit 1
fi

# Create a ZIP file for deployment
ZIP_FILE="deployment.zip"
pushd "$PUBLISH_DIR"
zip -r "../$ZIP_FILE" .
popd

echo "Deploying to Azure App Service: $APP_SERVICE_NAME..."

# Deploy to Azure App Service
az webapp deployment source config-zip \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_SERVICE_NAME" \
    --src "$ZIP_FILE"

# Check if deployment was successful
if [ $? -eq 0 ]; then
    echo "Deployment to App Service completed successfully."
else
    echo "Error: Failed to deploy to App Service"
    exit 1
fi

# Clean up
rm -f "$ZIP_FILE"
rm -rf "$PUBLISH_DIR"
popd

echo "Deployment process completed."