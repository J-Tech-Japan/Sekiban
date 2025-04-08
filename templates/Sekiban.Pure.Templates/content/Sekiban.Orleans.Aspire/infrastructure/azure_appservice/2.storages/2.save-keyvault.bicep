@description('The name of the existing storage account for Orleans')
param storageAccountName string = 'storage${replace(resourceGroup().name, '-', '')}'

@description('The name of the existing Key Vault to store secrets')
param keyVaultName string = 'kv-${resourceGroup().name}'

// Reference the existing storage account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Reference the existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Get the storage account connection string
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

// Store Orleans Clustering connection string in existing Key Vault
resource orleansClusteringConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'OrleansClusteringConnectionString' // Consider parameterizing secret names if needed
  properties: {
    value: storageConnectionString
  }
}

// Store Orleans Grain State connection string in existing Key Vault
resource orleansGrainStateConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'OrleansGrainStateConnectionString' // Consider parameterizing secret names if needed
  properties: {
    value: storageConnectionString
  }
}

// Store Orleans Queue connection string in existing Key Vault
resource orleansQueueConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'OrleansQueueConnectionString' // Consider parameterizing secret names if needed
  properties: {
    value: storageConnectionString
  }
}

// Outputs
output clusteringSecretName string = orleansClusteringConnectionStringSecret.name
output grainStateSecretName string = orleansGrainStateConnectionStringSecret.name
output queueSecretName string = orleansQueueConnectionStringSecret.name
output keyVaultName string = keyVault.name
