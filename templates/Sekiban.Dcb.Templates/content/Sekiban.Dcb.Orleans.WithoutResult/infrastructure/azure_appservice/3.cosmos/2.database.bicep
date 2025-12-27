@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The database name to create in the Cosmos DB account')
param sekibanDbName string = 'SekibanDcb'

resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

resource sekibanDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: sekibanDbAccount
  name: sekibanDbName
  properties: {
    resource: {
      id: sekibanDbName
    }
  }
}

output sekibanDatabaseName string = sekibanDatabase.name
