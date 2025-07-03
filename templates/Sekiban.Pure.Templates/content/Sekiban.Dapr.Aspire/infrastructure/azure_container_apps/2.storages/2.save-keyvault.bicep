@description('The name of the existing storage account for Dapr')
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

// Store Dapr State Store connection string in existing Key Vault
resource daprStateStoreConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DaprStateStoreConnectionString'
  properties: {
    value: storageConnectionString
  }
}

// Store Dapr State Store account name in existing Key Vault
resource daprStateStoreAccountNameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DaprStateStoreAccountName'
  properties: {
    value: storageAccount.name
  }
}

// Store Dapr State Store account key in existing Key Vault
resource daprStateStoreAccountKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DaprStateStoreAccountKey'
  properties: {
    value: storageAccount.listKeys().keys[0].value
  }
}

// Store Dapr Pub/Sub connection string in existing Key Vault
resource daprPubSubConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'DaprPubSubConnectionString'
  properties: {
    value: storageConnectionString
  }
}

// Outputs
output stateStoreConnectionSecretName string = daprStateStoreConnectionStringSecret.name
output stateStoreAccountNameSecretName string = daprStateStoreAccountNameSecret.name
output stateStoreAccountKeySecretName string = daprStateStoreAccountKeySecret.name
output pubSubConnectionSecretName string = daprPubSubConnectionStringSecret.name
output keyVaultName string = keyVault.name