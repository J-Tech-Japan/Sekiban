param cosmosAccountName string = 'cosmos-${resourceGroup().name}'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosAccountName
}

resource daprDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: cosmosAccount
  name: 'sekiban-dapr'
  properties: {
    resource: {
      id: 'sekiban-dapr'
    }
    // Note: No throughput options for serverless Cosmos DB accounts
  }
}

output databaseName string = daprDatabase.name