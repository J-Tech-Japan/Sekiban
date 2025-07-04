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
    options: {
      throughput: 400
    }
  }
}

output databaseName string = daprDatabase.name