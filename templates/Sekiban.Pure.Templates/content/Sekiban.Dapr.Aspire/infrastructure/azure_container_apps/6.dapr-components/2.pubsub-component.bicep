@description('The type of Dapr pub/sub to use')
@allowed(['azurestoragequeues', 'azureservicebus'])
param daprPubSubType string = 'azurestoragequeues'

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

// Create Dapr pub/sub component for Azure Storage Queues (as binding)
resource daprPubSubBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = if (daprPubSubType == 'azurestoragequeues') {
  parent: managedEnvironment
  name: 'pubsub-binding'
  properties: {
    componentType: 'bindings.azure.storagequeues'
    version: 'v1'
    metadata: [
      {
        name: 'accountName'
        value: storageAccount.name
      }
      {
        name: 'queueName'
        value: 'daprpubsub'
      }
      {
        name: 'accountKey'
        secretRef: 'dapr-storage-key'
      }
      {
        name: 'decodeBase64'
        value: 'true'
      }
      {
        name: 'ttlInSeconds'
        value: '300'
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

// Note: For true pub/sub functionality, Azure Service Bus would be required
// This would need additional Service Bus resources to be created

// Output
output pubSubComponentName string = daprPubSubBinding.name