param appServiceName string = 'backend-${resourceGroup().name}'
@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('Database type to use (cosmos or postgres)')
@allowed(['cosmos', 'postgres'])
param databaseType string = 'cosmos'

// データベース接続文字列のパラメータ名（アプリケーション設定用）
var databaseConnectionStringName = databaseType == 'postgres' 
  ? 'SekibanPostgres' 
  : 'SekibanCosmos'
var databaseConnectionStringSecretName = databaseType == 'postgres' 
  ? 'SekibanPostgresConnectionString' 
  : 'SekibanCosmosDbConnectionString'


var orleansClusteringConnectionStringSecretName = 'OrleansClusteringConnectionString'
var orleansGrainStateConnectionStringSecretName = 'OrleansGrainStateConnectionString'
var orleansQueueConnectionStringSecretName = 'OrleansQueueConnectionString'
// Reference to the existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Update the App Service with connection strings
resource connectionStringsConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: webApp
  name: 'connectionstrings'
  properties: {
    '${databaseConnectionStringName}': {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${databaseConnectionStringSecretName}/)'
      type: 'Custom'
    }
    OrleansSekibanClustering: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${orleansClusteringConnectionStringSecretName}/)'
      type: 'Custom'
    }
    OrleansSekibanGrainState: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${orleansGrainStateConnectionStringSecretName}/)'
      type: 'Custom'
    }
    OrleansSekibanQueue: {
      value: '@Microsoft.KeyVault(SecretUri=https://${keyVaultName}.vault.azure.net/secrets/${orleansQueueConnectionStringSecretName}/)'
      type: 'Custom'
    }
  }
}
