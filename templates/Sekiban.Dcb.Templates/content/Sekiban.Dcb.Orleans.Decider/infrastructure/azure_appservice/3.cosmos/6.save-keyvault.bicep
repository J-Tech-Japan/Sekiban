@description('The name of the existing first Cosmos DB account (for SekibanDb)')
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

@description('The name of the existing Key Vault to store secrets')
param keyVaultName string = 'kv-${resourceGroup().name}'

resource sekibanDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource sekibanDbConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbConnectionString'
  properties: {
    value: sekibanDbAccount.listConnectionStrings().connectionStrings[0].connectionString
  }
}

resource sekibanDbEndpointSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SekibanCosmosDbEndpoint'
  properties: {
    value: sekibanDbAccount.properties.documentEndpoint
  }
}

output mapDbConnectionStringSecretName string = sekibanDbConnectionStringSecret.name
output mapDbEndpointSecretName string = sekibanDbEndpointSecret.name
output keyVaultName string = keyVault.name
