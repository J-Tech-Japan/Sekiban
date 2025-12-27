@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the first Cosmos DB account for SekibanDb')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

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

output mapDbAccountName string = sekibanDbAccount.name
output sekibanDbAccountEndpoint string = sekibanDbAccount.properties.documentEndpoint
