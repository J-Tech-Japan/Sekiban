@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

param orleansClusterType string = 'cosmos'
param orleansDefaultGrainType string = 'cosmos'

// Orleans database resource - only create when orleansClusterType is not 'cosmos' but orleansDefaultGrainType is 'cosmos'
resource orleansDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = if (orleansClusterType != 'cosmos' && orleansDefaultGrainType == 'cosmos') {
  name: '${cosmosDbAccountName}/Orleans'
  properties: {
    resource: {
      id: 'Orleans'
    }
  }
}

// Orleans cluster container - conditionally create based on orleansDefaultGrainType
resource orleansClusterContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = if (orleansDefaultGrainType == 'cosmos') {
#disable-next-line use-parent-property
  name: '${cosmosDbAccountName}/Orleans/OrleansStorage'
  properties: {
    resource: {
      id: 'OrleansStorage'
      partitionKey: {
        paths: [
          '/PartitionKey'
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
