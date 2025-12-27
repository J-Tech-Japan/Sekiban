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

echo "Using resource group: $RESOURCE_GROUP in location: $LOCATION"
echo "Key Vault name: $KEYVAULT_NAME"

# Check and grant Key Vault access permissions
echo "Checking access permissions for Key Vault..."
CURRENT_USER_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)

# Set permissions using access policy mode
if [ -n "$CURRENT_USER_OBJECT_ID" ]; then
  echo "Granting Key Vault Secret management permissions to current user..."
  az keyvault set-policy --name $KEYVAULT_NAME --object-id $CURRENT_USER_OBJECT_ID --secret-permissions get set list delete backup restore recover purge
  echo "Access permissions granted successfully."
else
  echo "Warning: Could not retrieve current user information. You may not have sufficient permissions to access the Key Vault."
fi
echo "Key Vault access permissions check completed."