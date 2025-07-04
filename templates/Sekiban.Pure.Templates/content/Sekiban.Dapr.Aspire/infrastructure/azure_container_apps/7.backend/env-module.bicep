var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')

@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param databaseType string = 'cosmos'


param aspNetCoreEnvironment string = 'Production'

param applicationInsightsName string = 'ai-${resourceGroup().name}'

// Database connection string parameter names (for application settings)
var databaseConnectionStringName = databaseType == 'postgres' ? 'SekibanPostgres' : 'SekibanCosmos'
var databaseConnectionStringSecretName = databaseType == 'postgres'
  ? 'SekibanPostgresConnectionString'
  : 'SekibanCosmosDbConnectionString'

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

// Variables set to Container secrets
var secretVars = [
  {
    name: 'acr-password'
    value: acr.listCredentials().passwords[0].value
  }
  {
    name: 'database-connection-string-name'
    keyVaultUrl: 'https://${keyVaultName}${environment().suffixes.keyvaultDns}/secrets/${databaseConnectionStringSecretName}'
    identity: 'System'
  }
]

// Variables set to Container env
var envVars = [
  {
    name: 'ConnectionStrings__${databaseConnectionStringName}'
    secretRef: 'database-connection-string-name'
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
]

output secretVars array = secretVars
output envVars array = envVars