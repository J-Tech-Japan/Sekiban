#!/bin/bash

# parameter $1 is the name of the environment
# parameter $2 is the relative path to the bicep file

if [ -z "$1" ] || [ -z "$2" ]; then
    echo "Usage: $0 <environment-name> <path-to-bicep-file>"
    exit 1
fi

ENVIRONMENT=$1
BICEP_FILE=$2
CONFIG_FILE="${ENVIRONMENT}.local.json"

# Check if config file exists
if [ ! -f "$CONFIG_FILE" ]; then
    echo "Error: Configuration file $CONFIG_FILE not found"
    exit 1
fi

# get resource group name from {environment}.local.json parameter name is "resourceGroupName"
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_FILE")

# get location name from {environment}.local.json parameter name is "location"
LOCATION=$(jq -r '.location' "$CONFIG_FILE")

echo "Deploying to resource group: $RESOURCE_GROUP in location: $LOCATION"

# run az deployment
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file "$BICEP_FILE" 