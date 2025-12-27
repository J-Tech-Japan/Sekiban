#!/bin/bash

# parameter $1 is the name of the environment

if [ -z "$1" ]; then
    echo "Usage: $0 <environment-name>"
    exit 1
fi

ARG_INPUT="$1"

# Support both: (1) environment name (e.g., "env1") and (2) full/relative path to "*.local.json"
if [[ "$ARG_INPUT" == *.local.json ]]; then
    # When a file path is provided, extract basename and environment
    BASENAME="${ARG_INPUT##*/}"
    ENVIRONMENT="${BASENAME%.local.json}"
    CONFIG_FILE="$BASENAME"        # keep just the filename as requested
    CONFIG_PATH="$ARG_INPUT"       # use the provided path to read the file
else
    # When an environment name is provided, use {environment}.local.json in current directory
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
# get location name from {environment}.local.json parameter name is "location"
LOCATION=$(jq -r '.location' "$CONFIG_PATH")

echo "Create resource group: $RESOURCE_GROUP in location: $LOCATION"

# Create resource group if it doesn't exist
az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
echo "Resource group '$RESOURCE_GROUP' created or confirmed in location: $LOCATION"