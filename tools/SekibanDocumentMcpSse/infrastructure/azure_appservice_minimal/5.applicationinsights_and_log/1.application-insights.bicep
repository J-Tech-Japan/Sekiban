// Create Application Insights resource
param applicationInsightsName string = 'ai-${resourceGroup().name}'
param location string = resourceGroup().location
param kind string = 'web'
param applicationType string = 'web'

// Create Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: kind
  properties: {
    Application_Type: applicationType
    Request_Source: 'rest'
    DisableIpMasking: false
    Flow_Type: 'Redfield'
    HockeyAppId: ''
    SamplingPercentage: 100
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Output Application Insights Instrumentation Key and Connection String
output applicationInsightsInstrumentationKey string = applicationInsights.properties.InstrumentationKey
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString