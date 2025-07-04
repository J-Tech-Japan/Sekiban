param managedEnvName string = 'env-${resourceGroup().name}'
param cosmosAccountName string = 'cosmos-${resourceGroup().name}'

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

resource stateStoreComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnv
  name: 'statestore'
  properties: {
    componentType: 'state.azure.cosmosdb'
    version: 'v1'
    metadata: [
      {
        name: 'url'
        value: 'https://${cosmosAccountName}.documents.azure.com:443/'
      }
      {
        name: 'database'
        value: 'sekiban-dapr'
      }
      {
        name: 'collection'
        value: 'actors'
      }
      {
        name: 'masterKey'
        secretRef: 'cosmos-master-key'
      }
      {
        name: 'actorStateStore'
        value: 'true'
      }
    ]
    scopes: [
      'daprsekiban-apiservice'
    ]
    secrets: [
      {
        name: 'cosmos-master-key'
        keyVaultUrl: 'https://kv-${resourceGroup().name}${environment().suffixes.keyvaultDns}/secrets/CosmosDbMasterKey'
        identity: 'system'
      }
    ]
  }
}

output componentName string = stateStoreComponent.name