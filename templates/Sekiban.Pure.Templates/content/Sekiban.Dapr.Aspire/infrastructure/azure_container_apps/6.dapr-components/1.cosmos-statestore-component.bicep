param managedEnvName string = 'env-${resourceGroup().name}'
param cosmosAccountName string = 'cosmos-${resourceGroup().name}'

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosAccountName
}

// Get the Cosmos DB master key
var cosmosMasterKey = cosmosAccount.listKeys().primaryMasterKey

resource stateStoreComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnv
  name: 'sekiban-eventstore'
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
      'daprsekiban-eventrelay'
    ]
    secrets: [
      {
        name: 'cosmos-master-key'
        value: cosmosMasterKey
      }
    ]
  }
}

output componentName string = stateStoreComponent.name