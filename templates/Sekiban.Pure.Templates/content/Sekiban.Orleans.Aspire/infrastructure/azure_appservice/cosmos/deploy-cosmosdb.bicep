@description('The environment name suffix to add to resource names')
param environmentName string = 'staging'

@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the first Cosmos DB account for SekibanDb')
param mapDbAccountName string = 'cosmos-map-${environmentName}'

// Create the first Cosmos DB account with serverless configuration for SekibanDb
resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: mapDbAccountName
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
output mapDbAccountName string = sekibanDbAccount.name
output sekibanDbAccountEndpoint string = sekibanDbAccount.properties.documentEndpoint
