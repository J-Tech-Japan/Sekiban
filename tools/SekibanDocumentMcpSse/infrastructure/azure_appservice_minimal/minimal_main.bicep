// Main Bicep file to deploy all resources for Sekiban Document MCP on Azure App Service
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Azure OpenAI
module azureOpenAICreate '2.azureopenai/1.azure-openai.bicep' = {
  name: 'azureOpenAICreateDeployment'
  params: {}
}

module azureOpenAIKeyVaultSecrets '2.azureopenai/2.keyvault-secrets.bicep' = {
  name: 'azureOpenAIKeyVaultSecretsDeployment'
  params: {
    azureOpenAIEndpoint: azureOpenAICreate.outputs.azureOpenAIEndpoint
    azureOpenAIApiKey: azureOpenAICreate.outputs.azureOpenAIApiKey
    gptDeploymentName: azureOpenAICreate.outputs.gptDeploymentName
    embeddingDeploymentName: azureOpenAICreate.outputs.embeddingDeploymentName
  }
  dependsOn: [
    keyVaultCreate
    azureOpenAICreate
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
    appInsightsCreate
    mcpAppServiceCreate
    azureOpenAIKeyVaultSecrets
  ]
}

// Output the MCP App Service hostname and Azure OpenAI information
output mcpHostName string = mcpAppServiceCreate.outputs.appServiceHostName
output azureOpenAIEndpoint string = azureOpenAICreate.outputs.azureOpenAIEndpoint
output azureOpenAIName string = azureOpenAICreate.outputs.azureOpenAIName
output gptDeploymentName string = azureOpenAICreate.outputs.gptDeploymentName
output embeddingDeploymentName string = azureOpenAICreate.outputs.embeddingDeploymentName
