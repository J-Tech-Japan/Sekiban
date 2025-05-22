// Create Cosmos DB Container
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'
param databaseName string = 'SekibanDb'
param containerName string = 'Events'
param partitionKeyPath string = '/AggregateId'

// Reference to existing Cosmos DB account
resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Reference to existing Cosmos DB database
resource cosmosDbDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' existing = {
  name: databaseName
  parent: cosmosDbAccount
}

// Create Cosmos DB container
resource cosmosDbContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  name: containerName
  parent: cosmosDbDatabase
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: [
          partitionKeyPath
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
  }
}

// Output container name
output containerName string = cosmosDbContainer.name