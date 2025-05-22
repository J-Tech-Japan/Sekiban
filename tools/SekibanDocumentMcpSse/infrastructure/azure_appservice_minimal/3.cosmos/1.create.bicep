// Create Cosmos DB Account
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'
param location string = resourceGroup().location
param defaultConsistencyLevel string = 'Session'
param maxStalenessPrefix int = 100000
param maxIntervalInSeconds int = 300

resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: cosmosDbAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    consistencyPolicy: {
      defaultConsistencyLevel: defaultConsistencyLevel
      maxStalenessPrefix: maxStalenessPrefix
      maxIntervalInSeconds: maxIntervalInSeconds
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    databaseAccountOfferType: 'Standard'
    enableAutomaticFailover: false
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

// Output Cosmos DB account properties
output cosmosDbAccountName string = cosmosDbAccount.name
output cosmosDbAccountEndpoint string = cosmosDbAccount.properties.documentEndpoint
output cosmosDbAccountKey string = listKeys(cosmosDbAccount.id, cosmosDbAccount.apiVersion).primaryMasterKey