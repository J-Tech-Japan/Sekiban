@description('The name of the existing Cosmos DB account')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The database name in the Cosmos DB account')
param sekibanDbName string = 'SekibanDb'

// Reference to the existing Cosmos DB database
resource eventsDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' existing = {
  name: '${cosmosDbAccountName}/${sekibanDbName}'
}

resource eventsEventsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: eventsDatabase
  name: 'events'
  properties: {
    resource: {
      id: 'events'
      partitionKey: {
        paths: [
          '/rootPartitionKey'
          '/aggregateGroup'
          '/partitionKey'
        ]
        kind: 'MultiHash'
        version: 2
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
  }
  dependsOn: [
    eventsDatabase
  ]
}