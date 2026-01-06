@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'
param containerAppName string = 'backend-${resourceGroup().name}'

// Reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// get existing Contaier Apps
resource containerApp 'Microsoft.App/containerApps@2023-05-01' existing = {
  name: containerAppName
}

// Grant the Contaier Apps access to Key Vault secrets
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: containerApp.identity.principalId
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
