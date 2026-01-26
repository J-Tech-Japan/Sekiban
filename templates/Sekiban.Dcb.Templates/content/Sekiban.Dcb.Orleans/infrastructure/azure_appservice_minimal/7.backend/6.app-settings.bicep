param appServiceName string = 'backend-${resourceGroup().name}'
@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param databaseType string = 'cosmos'

param orleansClusterType string = 'cosmos'
param orleansDefaultGrainType string = 'cosmos'

@description('Queue type for Orleans')
param orleansQueueType string = 'azurestorage' //'eventhub'

@description('Event Hub instance name')
param eventHubName string =  'eventhub-${resourceGroup().name}'

param aspNetCoreEnvironment string = 'Production'

param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Orleans parameters
param orleansClusterId string = 'orleans-cluster-${uniqueString('${resourceGroup().name}cluster')}'
param orleansClusteringProviderType string = 'AzureTableStorage'
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
    // Orleans cluster settings - only added if orleansClusterType is not 'cosmos'
    // When orleansClusterType is 'cosmos', these settings are completely omitted since they're controlled by code
    ...(orleansClusterType != 'cosmos' ? {
      Orleans__ClusterId: orleansClusterId
      Orleans__Clustering__ProviderType: orleansClusteringProviderType
      Orleans__Clustering__ServiceKey: orleansClusteringServiceKey
    } : {})
    ...(orleansClusterType == 'cosmos' ? {
      ORLEANS_CLUSTERING_TYPE: 'cosmos'
    } : {})
    ...(orleansDefaultGrainType != 'cosmos' ? {
      Orleans__GrainStorage__Default__ProviderType: orleansGrainStorageDefaultProviderType
      Orleans__GrainStorage__Default__ServiceKey: orleansGrainStorageDefaultServiceKey
    } : {})
    ...(orleansDefaultGrainType == 'cosmos' ? {
      ORLEANS_GRAIN_DEFAULT_TYPE: 'cosmos'
    } : {})
    Orleans__EnableDistributedTracing: string(orleansEnableDistributedTracing)
    Orleans__Endpoints__GatewayPort: string(orleansGatewayPort)
    Orleans__Endpoints__SiloPort: string(orleansSiloPort)
    Orleans__GrainStorage__OrleansSekibanQueue__ProviderType: orleansGrainStorageOrleansSekibanQueueProviderType
    Orleans__GrainStorage__OrleansSekibanQueue__ServiceKey: orleansGrainStorageOrleansSekibanQueueServiceKey
    Orleans__ServiceId: orleansServiceId
    // NOTE: Orleans Streaming settings disabled due to Orleans 10 keyed service resolution issues
    // Using in-memory streams instead. Uncomment when Orleans fixes GetRequiredKeyedService<QueueServiceClient>.
    // ...(orleansQueueType != 'eventhub' ? {
    //   Orleans__Streaming__OrleansSekibanQueue__ProviderType: orleansStreamingOrleansSekibanQueueProviderType
    //   Orleans__Streaming__OrleansSekibanQueue__ServiceKey: orleansStreamingOrleansSekibanQueueServiceKey
    // } : {})
    // EventHub settings - only added if orleansQueueType is 'eventhub'
    ...(orleansQueueType == 'eventhub' ? {
      ORLEANS_QUEUE_TYPE: 'eventhub'
      ORLEANS_QUEUE_EVENTHUB_NAME: eventHubName
    } : {})
  }
}
