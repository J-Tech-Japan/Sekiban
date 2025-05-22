// Create Cosmos DB Database
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'
param databaseName string = 'SekibanDb'
param throughput int = 400

// Reference to existing Cosmos DB account
resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Create Cosmos DB database
resource cosmosDbDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  name: databaseName
  parent: cosmosDbAccount
  properties: {
    resource: {
      id: databaseName
    }
    options: {
      throughput: throughput
    }
  }
}

// Output database name
output databaseName string = cosmosDbDatabase.name