// Configure app settings for App Service
param appServiceName string = '${resourceGroup().name}'
param applicationInsightsName string = 'ai-${resourceGroup().name}'
param aspNetCoreEnvironment string = 'Production'

// Reference to existing App Service
resource appService 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Reference to existing Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

// Get Application Insights instrumentation key and connection string
var applicationInsightsInstrumentationKey = applicationInsights.properties.InstrumentationKey
var applicationInsightsConnectionString = applicationInsights.properties.ConnectionString

// Configure app settings
resource appSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'appsettings'
  parent: appService
  properties: {
    ASPNETCORE_ENVIRONMENT: aspNetCoreEnvironment
    APPINSIGHTS_INSTRUMENTATIONKEY: applicationInsightsInstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    AzureOpenAI__Endpoint: '@Microsoft.KeyVault(SecretUri=https://kv-${resourceGroup().name}.vault.azure.net/secrets/AzureOpenAIEndpoint/)'
    AzureOpenAI__ApiKey: '@Microsoft.KeyVault(SecretUri=https://kv-${resourceGroup().name}.vault.azure.net/secrets/AzureOpenAIApiKey/)'
    AzureOpenAI__DeploymentName: '@Microsoft.KeyVault(SecretUri=https://kv-${resourceGroup().name}.vault.azure.net/secrets/AzureOpenAIDeploymentName/)'
    AzureOpenAI__EmbeddingDeploymentName: '@Microsoft.KeyVault(SecretUri=https://kv-${resourceGroup().name}.vault.azure.net/secrets/AzureOpenAIEmbeddingDeploymentName/)'
    Documentation__BasePath: ''
    Documentation__EnableFileWatcher: 'false'
  }
}
