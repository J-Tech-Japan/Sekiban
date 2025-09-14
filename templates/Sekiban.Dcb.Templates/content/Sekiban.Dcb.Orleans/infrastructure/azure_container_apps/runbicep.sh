#!/bin/bash

# parameter $1 is the name of the environment
# parameter $2 is the relative path to the bicep file

if [ -z "$1" ] || [ -z "$2" ]; then
    echo "Usage: $0 <environment-name> <path-to-bicep-file>"
    exit 1
fi

ARG_INPUT="$1"
BICEP_FILE=$2
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

echo "Deploying to resource group: $RESOURCE_GROUP in location: $LOCATION"

if [ "$BICEP_FILE" = "aca_main.bicep" ]; then
  SHARED_KEY=$(az monitor log-analytics workspace get-shared-keys \
    --resource-group $RESOURCE_GROUP \
    --workspace-name law-$RESOURCE_GROUP \
    --query primarySharedKey -o tsv)
  PARAMS="logAnalyticsSharedKey=$SHARED_KEY"

  az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$BICEP_FILE" \
    --parameters "$PARAMS"
else
  echo "No set logAnalyticsSharedKey"

  az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$BICEP_FILE"
fi
