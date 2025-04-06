@description('The environment name suffix to add to resource names')
param environmentName string = 'staging'

@description('The Azure region for deploying resources')
param location string = resourceGroup().location

@description('The name of the first Cosmos DB account for SekibanDb')
param mapDbAccountName string = 'cosmos-map-${environmentName}'

@description('The name of the second Cosmos DB account for events')
param municipalDbAccountName string = 'cosmos-municipal-${environmentName}'

@description('The database name for the first Cosmos DB account')
param sekibanDbName string = 'SekibanDb'

@description('The name of the existing Key Vault to store secrets')
param keyVaultName string = 'kv-sekiban-${environmentName}'

@description('Enable Key Vault integration')
param enableKeyVaultIntegration bool = true

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (enableKeyVaultIntegration) {
  name: keyVaultName
}

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

// Create the second Cosmos DB account with serverless configuration for events
resource eventsDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: municipalDbAccountName
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

// Create SekibanDb database in the first Cosmos DB account
resource sekibanDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: sekibanDbAccount
  name: sekibanDbName
  properties: {
    resource: {
      id: sekibanDbName
    }
  }
}

// Create events database in the second Cosmos DB account
resource eventsDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: eventsDbAccount
  name: sekibanDbName
  properties: {
    resource: {
      id: sekibanDbName
    }
  }
}

// Create events container in eventsDb with hierarchical partitioning
resource eventsEventsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  name: '${municipalDbAccountName}/${sekibanDbName}/events'
  properties: {
    resource: {
      id: 'events'
      partitionKey: {
        paths: [
          '/rootPartitionKey'
          '/aggregateGroup'
          '/partitionKey'
        ]
        kind: 'MultiHash'
        version: 2
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
  dependsOn: [
    eventsDatabase
  ]
}

// Store SekibanDb Cosmos DB connection string in Key Vault
resource sekibanDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVaultIntegration) {
  parent: keyVault
  name: 'MapScanCosmosDbConnectionString'
  properties: {
    value: sekibanDbAccount.listConnectionStrings().connectionStrings[0].connectionString
  }
}

// Store SekibanDb Cosmos DB endpoint in Key Vault
resource sekibanDbEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVaultIntegration) {
  parent: keyVault
  name: 'MapScanCosmosDbEndpoint'
  properties: {
    value: sekibanDbAccount.properties.documentEndpoint
  }
}

// Store Events Cosmos DB connection string in Key Vault
resource eventsDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVaultIntegration) {
  parent: keyVault
  name: 'MunicipalCosmosDbConnectionString'
  properties: {
    value: eventsDbAccount.listConnectionStrings().connectionStrings[0].connectionString
  }
}

// Store Events Cosmos DB endpoint in Key Vault
resource eventsDbEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVaultIntegration) {
  parent: keyVault
  name: 'MunicipalCosmosDbEndpoint'
  properties: {
    value: eventsDbAccount.properties.documentEndpoint
  }
}

// Outputs
output mapDbAccountName string = sekibanDbAccount.name
output sekibanDbAccountEndpoint string = sekibanDbAccount.properties.documentEndpoint
// Removed output of sekibanDbConnectionString to avoid exposing secrets
output municipalDbAccountName string = eventsDbAccount.name
output eventsDbAccountEndpoint string = eventsDbAccount.properties.documentEndpoint
// Removed output of eventsDbConnectionString to avoid exposing secrets
output keyVaultName string = enableKeyVaultIntegration ? keyVault.name : 'No Key Vault integration enabled'
output keyVaultIntegrationStatus string = enableKeyVaultIntegration 
  ? '既存KeyVault "${keyVault.name}" を使用しました'
  : 'KeyVault統合は無効化されています'
