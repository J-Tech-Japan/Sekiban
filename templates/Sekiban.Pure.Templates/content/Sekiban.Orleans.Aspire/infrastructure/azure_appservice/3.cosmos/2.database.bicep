@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The database name to create in the Cosmos DB account')
param sekibanDbName string = 'SekibanDb'

// Reference the existing first Cosmos DB account
resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Create SekibanDb database in the Cosmos DB account
resource sekibanDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: sekibanDbAccount
  name: sekibanDbName
  properties: {
    resource: {
      id: sekibanDbName
    }
  }
}

// Outputs
output sekibanDatabaseName string = sekibanDatabase.name
