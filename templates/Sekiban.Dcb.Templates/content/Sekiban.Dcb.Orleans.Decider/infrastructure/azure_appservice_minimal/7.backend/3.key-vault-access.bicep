@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'
param appServiceName string = 'backend-${resourceGroup().name}'

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// get existing App Service
resource webApp 'Microsoft.Web/sites@2022-09-01' existing = {
  name: appServiceName
}

// Grant the App Service access to Key Vault secrets
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: webApp.identity.tenantId
        objectId: webApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
          keys: [
            'get'
            'list'
          ]
          certificates: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
}
