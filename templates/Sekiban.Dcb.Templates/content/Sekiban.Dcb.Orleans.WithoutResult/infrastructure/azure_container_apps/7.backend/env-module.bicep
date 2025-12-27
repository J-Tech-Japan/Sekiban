var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')

@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param databaseType string = 'cosmos'

param orleansClusterType string = 'cosmos'

@description('Queue type for Orleans')
param orleansQueueType string = 'eventhub' //'azurestorage'

// Database connection string parameter names (for application settings)
var databaseConnectionStringName = databaseType == 'postgres' ? 'DcbPostgres' : 'SekibanDcbCosmos'
var databaseConnectionStringSecretName = databaseType == 'postgres'
  ? 'SekibanPostgresConnectionString'
  : 'SekibanCosmosDbConnectionString'

var orleansClusteringConnectionStringName = orleansClusterType == 'cosmos' ? 'OrleansCosmos' : 'DcbOrleansClusteringTable'
var orleansClusteringConnectionStringSecretName = orleansClusterType == 'cosmos'
  ? 'SekibanCosmosDbConnectionString'
  : 'OrleansClusteringConnectionString'

var orleansGrainStateConnectionStringSecretName = 'OrleansGrainStateConnectionString'
var orleansQueueConnectionStringSecretName = orleansQueueType == 'eventhub'
  ? 'EventHubConnectionString'
  : 'OrleansQueueConnectionString'
var orleansQueueConnectionStringName = orleansQueueType == 'eventhub' ? 'OrleansEventHub' : 'DcbOrleansQueue'

@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param orleansDefaultGrainType string = 'cosmos'

@description('Event Hub instance name')
param eventHubName string = 'eventhub-${resourceGroup().name}'

param aspNetCoreEnvironment string = 'Production'

param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Orleans parameters
param orleansClusterId string = 'orleans-cluster-${uniqueString('${resourceGroup().name}cluster')}'
param orleansClusteringProviderType string = 'AzureTableStorage'
param orleansClusteringServiceKey string = 'MyProjectClustering'
param orleansEnableDistributedTracing bool = true
param orleansGatewayPort int = 30000
param orleansSiloPort int = 11111
param orleansGrainStorageDefaultProviderType string = 'AzureBlobStorage'
param orleansGrainStorageDefaultServiceKey string = 'MyProjectGrainState'
param orleansServiceId string = 'orleans-service-${uniqueString('${resourceGroup().name}service')}'
param orleansStreamingMyProjectQueueProviderType string = 'AzureQueueStorage'
param orleansStreamingMyProjectQueueServiceKey string = 'MyProjectQueue'

// get existing Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}
// get instrumentation key
var applicationInsightsInstrumentationKey = applicationInsights.properties.InstrumentationKey
// get connection string
var applicationInsightsConnectionString = applicationInsights.properties.ConnectionString

resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: acrName
}

var myProjectTableEnvSecret = orleansQueueType == 'eventhub'? [
  {
    name: 'myproject-table-secret'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/OrleansClusteringConnectionString'
    identity: 'System'
  }
] : []

var myProjectTableEnv = orleansQueueType == 'eventhub'? [
  {
    name: 'MyProjectTable'
    secretRef: 'myproject-table-secret'
  }
] : []

var orleansClusterEnv = orleansClusterType != 'cosmos'? [
  {
    name: 'Orleans__ClusterId'
    value: orleansClusterId
  }
  {
    name: 'Orleans__Clustering__ProviderType'
    value: orleansClusteringProviderType
  }
  {
    name: 'Orleans__Clustering__ServiceKey'
    value: orleansClusteringServiceKey
  }
] : [] 

var orleansClusterTypeEnv = orleansClusterType == 'cosmos'? [
  {
    name: 'ORLEANS_CLUSTERING_TYPE'
    value: 'cosmos'
  }
] : []

var orleansGrainStorageDefaultEnv = orleansDefaultGrainType != 'cosmos'? [
  {
    name: 'Orleans__GrainStorage__Default__ProviderType'
    value: orleansGrainStorageDefaultProviderType
  }
  {
    name: 'Orleans__GrainStorage__Default__ServiceKey'
    value: orleansGrainStorageDefaultServiceKey
  }
] : []

var orleansDefaultGrainTypeEnv = orleansDefaultGrainType == 'cosmos'? [
  {
    name: 'ORLEANS_GRAIN_DEFAULT_TYPE'
    value: 'cosmos'
  }
] : []

var orleansStreamingMyProjectQueueEnv = orleansQueueType != 'eventhub'? [
  {
    name: 'Orleans__Streaming__MyProjectQueue__ProviderType'
    value: orleansStreamingMyProjectQueueProviderType
  }
  {
    name: 'Orleans__Streaming__MyProjectQueue__ServiceKey'
    value: orleansStreamingMyProjectQueueServiceKey
  }
] : []

var orleansQueueEnv = orleansQueueType == 'eventhub'? [
  {
    name: 'ORLEANS_QUEUE_TYPE'
    value: 'eventhub'
  }
  {
    name: 'ORLEANS_QUEUE_EVENTHUB_NAME'
    value: eventHubName
  }
] : []

// Variables set to Container secrets
var secretVars = concat([
  {
    name: 'acr-password'
    value: acr.listCredentials().passwords[0].value
  }
  {
    name: 'database-connection-string-name'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/${databaseConnectionStringSecretName}'
    identity: 'System'
  }
  {
    name: 'orleans-clustering-connection-string-name'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/${orleansClusteringConnectionStringSecretName}'
    identity: 'System'
  }
  // Always expose a Storage Table connection string for Orleans table usages
  // (checkpointer, PubSub, etc.) regardless of clustering type.
  {
    name: 'table-connection-string-name'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/OrleansClusteringConnectionString'
    identity: 'System'
  }
  {
    name: 'myproject-grain-state-secret'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/${orleansGrainStateConnectionStringSecretName}'
    identity: 'System'
  }
  {
    name: 'orleans-queue-connection-string-name'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/${orleansQueueConnectionStringSecretName}'
    identity: 'System'
  }
  ],
  myProjectTableEnvSecret
)

// Variables set to Container env
var envVars = concat([
  {
    name: 'ConnectionStrings__${databaseConnectionStringName}'
    secretRef: 'database-connection-string-name'
  }
  {
    name: 'ConnectionStrings__${orleansClusteringConnectionStringName}'
    secretRef: 'orleans-clustering-connection-string-name'
  }
  {
    name: 'ConnectionStrings__DcbOrleansGrainTable'
    secretRef: 'table-connection-string-name'
  }
  {
    name: 'ConnectionStrings__DcbOrleansGrainState'
    secretRef: 'myproject-grain-state-secret'
  }
  {
    name: 'ConnectionStrings__MultiProjectionOffload'
    secretRef: 'myproject-grain-state-secret'
  }
  {
    name: 'ConnectionStrings__${orleansQueueConnectionStringName}'
    secretRef: 'orleans-queue-connection-string-name'
  }
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
    name: 'Sekiban__Database'
    value: databaseType
  }
  {
    name: 'Orleans__EnableDistributedTracing'
    value: string(orleansEnableDistributedTracing)
  }
  {
    name: 'Orleans__Endpoints__GatewayPort'
    value: string(orleansGatewayPort)
  }
  {
    name: 'Orleans__Endpoints__SiloPort'
    value: string(orleansSiloPort)
  }
  // Removed legacy MyProjectQueue grain storage provider env vars
  {
    name: 'Orleans__ServiceId'
    value: orleansServiceId
  }
  ], 
  myProjectTableEnv,
  orleansClusterEnv,
  orleansClusterTypeEnv,
  orleansGrainStorageDefaultEnv,
  orleansDefaultGrainTypeEnv,
  orleansStreamingMyProjectQueueEnv,
  orleansQueueEnv
)

output secretVars array = secretVars
output envVars array = envVars
