@description('The type of Dapr state store to use')
@allowed(['azureblobstorage', 'azuretablestorage'])
param daprStateStoreType string = 'azureblobstorage'

@description('The name of the existing storage account')
param storageAccountName string = 'storage${replace(resourceGroup().name, '-', '')}'

@description('The name of the existing Key Vault')
param keyVaultName string = 'kv-${resourceGroup().name}'

@description('The name of the existing managed environment')
param managedEnvironmentName string = 'env-${resourceGroup().name}'

// Reference existing resources
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvironmentName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Create Dapr state store component
resource daprStateStore 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnvironment
  name: 'statestore'
  properties: {
    componentType: 'state.azure.${daprStateStoreType}'
    version: 'v2'
    metadata: [
      {
        name: 'accountName'
        value: storageAccount.name
      }
      {
        name: 'containerName'
        value: 'daprstate'
      }
      {
        name: 'tableName'
        value: 'daprstate'
      }
      {
        name: 'accountKey'
        secretRef: 'dapr-storage-key'
      }
    ]
    secrets: [
      {
        name: 'dapr-storage-key'
        value: storageAccount.listKeys().keys[0].value
      }
    ]
    scopes: [
      'daprsekiban-apiservice'
    ]
  }
}

// Output
output stateStoreComponentName string = daprStateStore.name