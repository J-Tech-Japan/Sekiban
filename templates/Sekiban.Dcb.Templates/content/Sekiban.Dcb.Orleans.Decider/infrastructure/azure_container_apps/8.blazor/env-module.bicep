param containerAppNameBackend string = 'backend-${resourceGroup().name}'
param aspNetCoreEnvironment string = 'Production'
param applicationInsightsName string = 'ai-${resourceGroup().name}'

resource containerAppBackend 'Microsoft.App/containerApps@2023-05-01' existing = {
  name: containerAppNameBackend
}

// get existing Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}
// get instrumentation key
var applicationInsightsInstrumentationKey = applicationInsights.properties.InstrumentationKey
// get connection string
var applicationInsightsConnectionString = applicationInsights.properties.ConnectionString

// Variables set to Container env
var envVars = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: aspNetCoreEnvironment
  }
  {
    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
    value: applicationInsightsInstrumentationKey
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
  {
    name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
    value: '~3'
  }
  {
    name: 'services__apiservice__https__0'
    value: 'https://${containerAppBackend.properties.configuration.ingress.fqdn}'
  }
]

output envVars array = envVars
