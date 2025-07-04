#!/bin/bash

echo "Registering required Azure resource providers..."

# Register Microsoft.App provider (for Container Apps)
echo "Registering Microsoft.App..."
az provider register --namespace Microsoft.App --wait

# Register Microsoft.ContainerService provider (for Container Apps Environment)
echo "Registering Microsoft.ContainerService..."
az provider register --namespace Microsoft.ContainerService --wait

# Register Microsoft.OperationalInsights provider (for Log Analytics)
echo "Registering Microsoft.OperationalInsights..."
az provider register --namespace Microsoft.OperationalInsights --wait

# Register Microsoft.ServiceBus provider (for Service Bus)
echo "Registering Microsoft.ServiceBus..."
az provider register --namespace Microsoft.ServiceBus --wait

# Check registration status
echo ""
echo "Checking registration status..."
az provider show --namespace Microsoft.App --query "registrationState" -o tsv
az provider show --namespace Microsoft.ContainerService --query "registrationState" -o tsv
az provider show --namespace Microsoft.OperationalInsights --query "registrationState" -o tsv
az provider show --namespace Microsoft.ServiceBus --query "registrationState" -o tsv

echo ""
echo "Resource provider registration complete!"