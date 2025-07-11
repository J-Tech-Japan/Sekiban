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

# get Log analytics workspace name from {environment}.local.json parameter name is "resourceGroupName"
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_FILE")
# get location name from {environment}.local.json parameter name is "location"
LOCATION=$(jq -r '.location' "$CONFIG_FILE")

echo "Create Log analytics workspace: $RESOURCE_GROUP in location: $LOCATION"

# Create Log analytics workspace if it doesn't exist
az group create --name "$RESOURCE_GROUP" --location "$LOCATION"

az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file log-analytics/create.bicep

echo "Log analytics workspace 'law-$RESOURCE_GROUP' created or confirmed in location: $LOCATION"
