@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the Cosmos DB account for Sekiban')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

// Create the Cosmos DB account with serverless configuration for Sekiban
resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: cosmosDbAccountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

// Outputs
output cosmosDbAccountName string = sekibanDbAccount.name
output cosmosDbAccountEndpoint string = sekibanDbAccount.properties.documentEndpoint