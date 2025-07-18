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

# Get eventrelay relative path from config file (will be added to config)
EVENTRELAY_PATH=$(jq -r '.eventRelayRelativePath // "../../DaprSekiban.EventRelay"' "$CONFIG_FILE")

# Verify the eventrelay path exists
if [ ! -d "$EVENTRELAY_PATH" ]; then
    echo "Error: EventRelay directory not found at $EVENTRELAY_PATH"
    exit 1
fi

echo "EventRelay path: $EVENTRELAY_PATH"

# Container App name and ACR name will follow the naming convention
CONTAINER_APP_NAME="eventrelay-${RESOURCE_GROUP}"
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

# Create Dockerfile for EventRelay if it doesn't exist
DOCKERFILE_PATH="${EVENTRELAY_PATH#../../}/Dockerfile"
if [ ! -f "$DOCKERFILE_PATH" ]; then
    echo "Creating Dockerfile for EventRelay..."
    cat > "$DOCKERFILE_PATH" << 'EOF'
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5020
EXPOSE 5021

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DaprSekiban.EventRelay/DaprSekiban.EventRelay.csproj", "DaprSekiban.EventRelay/"]
COPY ["DaprSekiban.ServiceDefaults/DaprSekiban.ServiceDefaults.csproj", "DaprSekiban.ServiceDefaults/"]
COPY ["DaprSekiban.Domain/DaprSekiban.Domain.csproj", "DaprSekiban.Domain/"]
RUN dotnet restore "./DaprSekiban.EventRelay/DaprSekiban.EventRelay.csproj"
COPY . .
WORKDIR "/src/DaprSekiban.EventRelay"
RUN dotnet build "./DaprSekiban.EventRelay.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DaprSekiban.EventRelay.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DaprSekiban.EventRelay.dll"]
EOF
fi

docker buildx build --platform linux/amd64 -t "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest" -f "$DOCKERFILE_PATH" --push .

# Check if building and pushing image was successful (buildx pushes automatically)
if [ $? -eq 0 ]; then
    echo "Building and pushing Docker image completed successfully."
else
    echo "Error: Failed to build and push Docker image"
    exit 1
fi

# Update the container app with the new image (or show message if first deployment)
echo "Updating eventrelay container in Azure Container Apps..."
if az containerapp show --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" &>/dev/null; then
    az containerapp update --name "$CONTAINER_APP_NAME" --resource-group "$RESOURCE_GROUP" --image "$ACR_LOGIN_SERVER/$CONTAINER_APP_NAME:latest"
    if [ $? -eq 0 ]; then
        echo "EventRelay container updated successfully."
    else
        echo "Error: Failed to update eventrelay container"
        exit 1
    fi
else
    echo "Container app '$CONTAINER_APP_NAME' does not exist yet."
    echo "Please run './runbicep.sh $1 aca_apps.bicep' first to create the container apps."
    echo "The Docker image has been pushed successfully and is ready for deployment."
fi

echo "Deployment process completed."