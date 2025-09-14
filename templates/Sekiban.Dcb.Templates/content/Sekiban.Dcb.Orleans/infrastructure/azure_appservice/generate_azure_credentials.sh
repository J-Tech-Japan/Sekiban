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
# get location name from {environment}.local.json parameter name is "location"
LOCATION=$(jq -r '.location' "$CONFIG_PATH")

# Get the current subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Create service principal name based on environment
SP_NAME="sp-${ENVIRONMENT}-${RESOURCE_GROUP}"

echo "Creating service principal: $SP_NAME for resource group: $RESOURCE_GROUP"

# Create service principal with contributor access to the resource group
az ad sp create-for-rbac --name "$SP_NAME" --role contributor --scopes /subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP} --json-auth

echo "Service principal credentials created and saved to ${ENVIRONMENT}-credentials.json"
echo "IMPORTANT: Keep this file secure as it contains sensitive authentication information."