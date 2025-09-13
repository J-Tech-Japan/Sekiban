param appServiceName string = 'frontend-${resourceGroup().name}'
param appServiceNameBackend string = 'backend-${resourceGroup().name}'

param aspNetCoreEnvironment string = 'Production'

param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}
resource webAppBackend 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceNameBackend
}

// get existing Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}
// get instrumentation key
var applicationInsightsInstrumentationKey = applicationInsights.properties.InstrumentationKey
// get connection string
var applicationInsightsConnectionString = applicationInsights.properties.ConnectionString

// Update the App Service with app settings
resource appSettingsConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: webApp
  name: 'appsettings'
  properties: {
    ASPNETCORE_ENVIRONMENT: aspNetCoreEnvironment
    APPINSIGHTS_INSTRUMENTATIONKEY: applicationInsightsInstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsightsConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    services__apiservice__https__0 : 'https://${webAppBackend.properties.defaultHostName}'
  }
}
