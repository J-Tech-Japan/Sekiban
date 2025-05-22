// Grant App Service access to Key Vault
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

// Add access policy for App Service to Key Vault
resource appServiceAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2022-07-01' = {
  name: 'add'
  parent: keyVault
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: appService.identity.principalId
        permissions: {
          keys: ['get', 'list']
          secrets: ['get', 'list']
          certificates: ['get', 'list']
        }
      }
    ]
  }
}