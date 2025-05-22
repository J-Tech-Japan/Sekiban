// Set connection strings for App Service
param appServiceName string = 'mcp-${resourceGroup().name}'
param keyVaultName string = 'kv-${resourceGroup().name}'

// Reference to existing App Service
resource appService 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2021-06-01-preview' existing = {
  name: keyVaultName
}

// Set connection strings for App Service
resource connectionStrings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: 'connectionstrings'
  parent: appService
  properties: {
    AzureKeyVault: {
      value: keyVault.properties.vaultUri
      type: 'Custom'
    }
  }
}