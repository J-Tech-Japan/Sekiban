// Main Bicep file to deploy all resources for Sekiban Document MCP on Azure App Service
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Storages
module storageCreate '2.storages/1.create.bicep' = {
  name: 'storageCreateDeployment'
  params: {}
}

// 3. Cosmos DB
module cosmosCreate '3.cosmos/1.create.bicep' = {
  name: 'cosmosCreateDeployment'
  params: {}
}

module cosmosDatabase '3.cosmos/2.database.bicep' = {
  name: 'cosmosDatabaseDeployment'
  params: {}
  dependsOn: [
    cosmosCreate
  ]
}

module cosmosContainer '3.cosmos/3.container.bicep' = {
  name: 'cosmosContainerDeployment'
  params: {}
  dependsOn: [
    cosmosDatabase
  ]
}

// 5. Application Insights & Log Analytics
module appInsightsCreate '5.applicationinsights_and_log/1.application-insights.bicep' = {
  name: 'appInsightsCreateDeployment'
  params: {}
}

module logAnalyticsCreate '5.applicationinsights_and_log/2.log-analytics-workspace.bicep' = {
  name: 'logAnalyticsCreateDeployment'
  params: {}
}

// 7. MCP App Service
module mcpPlan '7.backend/1.plan.bicep' = {
  name: 'mcpPlanDeployment'
  params: {}
}

module mcpAppServiceCreate '7.backend/2.app-service-create.bicep' = {
  name: 'mcpAppServiceCreateDeployment'
  params: {}
  dependsOn: [
    mcpPlan
  ]
}

module mcpKeyVaultAccess '7.backend/3.key-vault-access.bicep' = {
  name: 'mcpKeyVaultAccessDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    mcpAppServiceCreate
  ]
}

module mcpConnectionStrings '7.backend/4.connection-strings.bicep' = {
  name: 'mcpConnectionStringsDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    mcpAppServiceCreate
    cosmosCreate
  ]
}

module mcpDiagnosticSettings '7.backend/5.diagnostic-settings.bicep' = {
  name: 'mcpDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    logAnalyticsCreate
    mcpAppServiceCreate
  ]
}

module mcpAppSettings '7.backend/6.app-settings.bicep' = {
  name: 'mcpAppSettingsDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    storageCreate
    cosmosCreate
    appInsightsCreate
    mcpAppServiceCreate
  ]
}

// Output the MCP App Service hostname
output mcpHostName string = mcpAppServiceCreate.outputs.appServiceHostName