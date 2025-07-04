param cosmosAccountName string = 'cosmos-${resourceGroup().name}'
param databaseName string = 'sekiban-dapr'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosAccountName
}

resource daprDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' existing = {
  parent: cosmosAccount
  name: databaseName
}

resource actorsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: daprDatabase
  name: 'actors'
  properties: {
    resource: {
      id: 'actors'
      partitionKey: {
        paths: ['/partitionKey']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
    }
    // Note: No throughput options for serverless Cosmos DB accounts
  }
}

output containerName string = actorsContainer.name