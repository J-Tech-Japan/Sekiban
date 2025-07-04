# Deployment script for Sekiban Dapr Aspire on Azure Container Apps

param(
    [Parameter(Mandatory=$true)]
    [string]$Environment
)

$ErrorActionPreference = "Stop"

$ConfigFile = "$Environment.local.json"

# Check if config file exists
if (-not (Test-Path $ConfigFile)) {
    Write-Error "Configuration file $ConfigFile not found"
    Write-Host "Please create a configuration file with the following structure:"
    Write-Host @"
{
  "resourceGroupName": "your-resource-group-name",
  "location": "japaneast"
}
"@
    exit 1
}

# Get configuration values
$Config = Get-Content $ConfigFile | ConvertFrom-Json
$ResourceGroup = $Config.resourceGroupName
$Location = $Config.location

Write-Host "================================"
Write-Host "Deploying Sekiban Dapr Aspire"
Write-Host "Environment: $Environment"
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Location: $Location"
Write-Host "================================"

# Step 1: Create Resource Group
Write-Host "Step 1: Creating Resource Group..."
& ".\create_resource_group.ps1" -Environment $Environment

# Step 2: Create Log Analytics Workspace
Write-Host "Step 2: Creating Log Analytics Workspace..."
& ".\create_log-analytics.ps1" -Environment $Environment

# Step 3: Create Container Registry
Write-Host "Step 3: Creating Container Registry..."
& ".\create_container_registry.ps1" -Environment $Environment

# Step 4: Deploy infrastructure with Bicep
Write-Host "Step 4: Deploying infrastructure..."
$SharedKey = az monitor log-analytics workspace get-shared-keys `
  --resource-group $ResourceGroup `
  --workspace-name "law-$ResourceGroup" `
  --query primarySharedKey -o tsv

az deployment group create `
  --resource-group $ResourceGroup `
  --template-file "aca_main.bicep" `
  --parameters "logAnalyticsSharedKey=$SharedKey" `
  --parameters "daprStateStoreType=azureblobstorage" `
  --parameters "daprPubSubType=azurestoragequeues"

# Step 5: Grant user access to Key Vault
Write-Host "Step 5: Granting user access to Key Vault..."
& ".\user_access_keyvault.ps1" -Environment $Environment

# Step 6: Deploy backend application
Write-Host "Step 6: Deploying backend application..."
Write-Host "Please build and push your backend container image first:"
Write-Host "  cd ..\..\..\DaprSekiban.ApiService"
Write-Host "  dotnet publish -c Release"
Write-Host "  docker build -t backend-$ResourceGroup ."
Write-Host "  docker tag backend-$ResourceGroup ${acrName}.azurecr.io/backend-${ResourceGroup}:latest"
Write-Host "  az acr login --name ${acrName}"
Write-Host "  docker push ${acrName}.azurecr.io/backend-${ResourceGroup}:latest"
Write-Host ""
Write-Host "Then run: .\code_deploy_backend.ps1 $Environment"

# Step 7: Deploy frontend application
Write-Host "Step 7: Deploying frontend application..."
Write-Host "Please build and push your frontend container image first:"
Write-Host "  cd ..\..\..\DaprSekiban.Web"
Write-Host "  dotnet publish -c Release"
Write-Host "  docker build -t blazor-$ResourceGroup ."
Write-Host "  docker tag blazor-$ResourceGroup ${acrName}.azurecr.io/blazor-${ResourceGroup}:latest"
Write-Host "  az acr login --name ${acrName}"
Write-Host "  docker push ${acrName}.azurecr.io/blazor-${ResourceGroup}:latest"
Write-Host ""
Write-Host "Then run: .\code_deploy_frontend.ps1 $Environment"

Write-Host "================================"
Write-Host "Deployment completed!"
Write-Host "================================"
Write-Host ""
Write-Host "Next steps:"
Write-Host "1. Build and push your container images"
Write-Host "2. Run the code deployment scripts"
Write-Host "3. Access your application at the provided URLs"