param appServiceName string = 'backend-${resourceGroup().name}'
@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param databaseType string = 'cosmos'

param aspNetCoreEnvironment string = 'Production'

param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Orleans parameters
param orleansClusterId string = 'orleans-cluster-${uniqueString('${resourceGroup().name}cluster')}'
param orleansClusteringProviderType string = 'AzureStorage'
param orleansClusteringServiceKey string = 'OrleansSekibanClustering'
param orleansEnableDistributedTracing bool = true
param orleansGatewayPort int = 30000
param orleansSiloPort int = 11111
param orleansGrainStorageDefaultProviderType string = 'AzureBlobStorage'
param orleansGrainStorageDefaultServiceKey string = 'OrleansSekibanGrainState'
param orleansGrainStorageOrleansSekibanQueueProviderType string = 'AzureBlobStorage'
param orleansGrainStorageOrleansSekibanQueueServiceKey string = 'OrleansSekibanGrainState'
param orleansServiceId string = 'orleans-service-${uniqueString('${resourceGroup().name}service')}'
param orleansStreamingOrleansSekibanQueueProviderType string = 'AzureQueueStorage'
param orleansStreamingOrleansSekibanQueueServiceKey string = 'OrleansSekibanQueue'

// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
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
    Sekiban__Database: databaseType
    Orleans__ClusterId: orleansClusterId
    Orleans__Clustering__ProviderType: orleansClusteringProviderType
    Orleans__Clustering__ServiceKey: orleansClusteringServiceKey
    Orleans__EnableDistributedTracing: string(orleansEnableDistributedTracing)
    Orleans__Endpoints__GatewayPort: string(orleansGatewayPort)
    Orleans__Endpoints__SiloPort: string(orleansSiloPort)
    Orleans__GrainStorage__Default__ProviderType: orleansGrainStorageDefaultProviderType
    Orleans__GrainStorage__Default__ServiceKey: orleansGrainStorageDefaultServiceKey
    Orleans__GrainStorage__OrleansSekibanQueue__ProviderType: orleansGrainStorageOrleansSekibanQueueProviderType
    Orleans__GrainStorage__OrleansSekibanQueue__ServiceKey: orleansGrainStorageOrleansSekibanQueueServiceKey
    Orleans__ServiceId: orleansServiceId
    Orleans__Streaming__OrleansSekibanQueue__ProviderType: orleansStreamingOrleansSekibanQueueProviderType
    Orleans__Streaming__OrleansSekibanQueue__ServiceKey: orleansStreamingOrleansSekibanQueueServiceKey
  }
}
