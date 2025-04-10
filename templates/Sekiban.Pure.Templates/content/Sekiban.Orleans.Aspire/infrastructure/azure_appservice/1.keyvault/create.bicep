@description('The name of the Key Vault.')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('The location where the Key Vault should be created.')
param location string = resourceGroup().location

@description('The Azure Active Directory tenant ID that should be used for authenticating requests to the Key Vault.')
param tenantId string = subscription().tenantId

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    accessPolicies: [
      // Add access policies here if needed, for example:
      // {
      //   tenantId: tenantId
      //   objectId: 'YOUR_PRINCIPAL_ID' // Replace with the Object ID of the user, group, or service principal
      //   permissions: {
      //     secrets: [
      //       'get'
      //       'list'
      //     ]
      //     keys: [
      //       'get'
      //     ]
      //     certificates: [
      //       'get'
      //     ]
      //   }
      // }
    ]
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
    enableRbacAuthorization: false // Set to true if using RBAC instead of access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
