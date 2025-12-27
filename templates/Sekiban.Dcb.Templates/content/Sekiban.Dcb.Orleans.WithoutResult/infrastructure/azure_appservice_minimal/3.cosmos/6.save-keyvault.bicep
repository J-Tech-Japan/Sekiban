@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The name of the existing Key Vault to store secrets')
param keyVaultName string = 'kv-${resourceGroup().name}'

// Reference the existing first Cosmos DB account
resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Store SekibanDb Cosmos DB connection string in Key Vault
resource sekibanDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbConnectionString' // Consider parameterizing secret names if needed
  properties: {
    value: sekibanDbAccount.listConnectionStrings().connectionStrings[0].connectionString
  }
}

// Store SekibanDb Cosmos DB endpoint in Key Vault
resource sekibanDbEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbEndpoint' // Consider parameterizing secret names if needed
  properties: {
    value: sekibanDbAccount.properties.documentEndpoint
  }
}

// Outputs
output mapDbConnectionStringSecretName string = sekibanDbConnectionStringSecret.name
output mapDbEndpointSecretName string = sekibanDbEndpointSecret.name
output keyVaultName string = keyVault.name
