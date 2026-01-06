#!/bin/bash

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

# get resource group name from {environment}.local.json parameter name is "resourceGroupName"
RESOURCE_GROUP=$(jq -r '.resourceGroupName' "$CONFIG_PATH")

# Extract Key Vault name from the create.bicep file
KV_NAME="kv-$RESOURCE_GROUP"

echo "Resource Group: $RESOURCE_GROUP"
echo "Key Vault Name: $KV_NAME"

# List deleted Key Vaults
echo "Checking for deleted Key Vaults..."
az keyvault list-deleted --query "[?name=='$KV_NAME']" -o table

# Purge the deleted Key Vault
echo "Purging Key Vault: $KV_NAME"
az keyvault purge --name "$KV_NAME" || echo "Key Vault not found in deleted state or cannot be purged"

echo "Key Vault purge operation completed."