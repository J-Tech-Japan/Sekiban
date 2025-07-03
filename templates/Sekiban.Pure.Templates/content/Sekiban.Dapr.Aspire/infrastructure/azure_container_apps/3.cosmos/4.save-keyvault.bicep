@description('The name of the existing Cosmos DB account')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The name of the existing Key Vault to store secrets')
param keyVaultName string = 'kv-${resourceGroup().name}'

// Reference the existing Cosmos DB account
resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Store Sekiban Cosmos DB connection string in Key Vault
resource sekibanDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbConnectionString'
  properties: {
    value: sekibanDbAccount.listConnectionStrings().connectionStrings[0].connectionString
  }
}

// Store Sekiban Cosmos DB endpoint in Key Vault
resource sekibanDbEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbEndpoint'
  properties: {
    value: sekibanDbAccount.properties.documentEndpoint
  }
}

// Outputs
output cosmosDbConnectionStringSecretName string = sekibanDbConnectionStringSecret.name
output cosmosDbEndpointSecretName string = sekibanDbEndpointSecret.name
output keyVaultName string = keyVault.name