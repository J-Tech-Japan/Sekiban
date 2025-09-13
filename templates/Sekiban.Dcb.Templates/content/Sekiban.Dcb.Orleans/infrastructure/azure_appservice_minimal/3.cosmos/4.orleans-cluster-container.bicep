@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

param orleansClusterType string = 'cosmos'

// Orleans database and container resources only when orleansClusterType is 'cosmos'
resource orleansDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = if (orleansClusterType == 'cosmos') {
  name: '${cosmosDbAccountName}/Orleans'
  properties: {
    resource: {
      id: 'Orleans'
    }
  }
}

resource orleansClusterContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (orleansClusterType == 'cosmos') {
  parent: orleansDatabase
  name: 'OrleansCluster'
  properties: {
    resource: {
      id: 'OrleansCluster'
      partitionKey: {
        paths: [
          '/ClusterId'
        ]
        kind: 'Hash'
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
}
