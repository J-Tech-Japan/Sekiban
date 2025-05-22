// Set connection strings for App Service
param appServiceName string = 'mcp-${resourceGroup().name}'
param keyVaultName string = 'kv-${resourceGroup().name}'
param cosmosDbAccountName string = 'cosmos-${resourceGroup().name}'

// Reference to existing App Service
resource appService 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2021-06-01-preview' existing = {
  name: keyVaultName
}

// Reference to existing Cosmos DB account
resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

// Get Cosmos DB primary connection string
var cosmosDbConnectionString = 'AccountEndpoint=${cosmosDbAccount.properties.documentEndpoint};AccountKey=${listKeys(cosmosDbAccount.id, cosmosDbAccount.apiVersion).primaryMasterKey}'

// Set connection strings for App Service
resource connectionStrings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'connectionstrings'
  parent: appService
  properties: {
    SekibanCosmosDB: {
      value: cosmosDbConnectionString
      type: 'Custom'
    }
    AzureKeyVault: {
      value: keyVault.properties.vaultUri
      type: 'Custom'
    }
  }
}