// Create Key Vault resource
param keyVaultName string = 'kv-${resourceGroup().name}'
param location string = resourceGroup().location
param enabledForDeployment bool = true
param enabledForDiskEncryption bool = true
param enabledForTemplateDeployment bool = true
param tenantId string = subscription().tenantId
param accessPolicies array = []
param sku object = {
  name: 'standard'
  family: 'A'
}
param enableRbacAuthorization bool = false

// Create Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2021-06-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    enabledForDeployment: enabledForDeployment
    enabledForDiskEncryption: enabledForDiskEncryption
    enabledForTemplateDeployment: enabledForTemplateDeployment
    tenantId: tenantId
    accessPolicies: accessPolicies
    sku: sku
    enableRbacAuthorization: enableRbacAuthorization
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Output the Key Vault URI
output keyVaultUri string = keyVault.properties.vaultUri