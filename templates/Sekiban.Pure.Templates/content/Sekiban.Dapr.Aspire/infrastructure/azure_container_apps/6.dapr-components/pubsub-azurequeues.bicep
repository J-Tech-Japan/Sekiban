@description('The name of the existing storage account')
param storageAccountName string = 'storage${replace(resourceGroup().name, '-', '')}'

@description('The name of the existing managed environment')
param managedEnvironmentName string = 'env-${resourceGroup().name}'

// Reference existing resources
resource managedEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvironmentName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Create Dapr component for Azure Storage Queues
// Note: This uses the in-memory pub/sub with Azure Storage Queues as a binding workaround
// For production use, Azure Service Bus is recommended for true pub/sub functionality
resource daprPubSubComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnvironment
  name: 'pubsub'
  properties: {
    componentType: 'pubsub.in-memory'
    version: 'v1'
    metadata: []
    scopes: [
      'daprsekiban-apiservice'
      'daprsekiban-web'
    ]
  }
}

// Create Azure Storage Queue binding for persistence (optional)
resource daprQueueBinding 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnvironment
  name: 'azurequeue-binding'
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
        value: 'dapr-events'
      }
      {
        name: 'accountKey'
        secretRef: 'storage-account-key'
      }
      {
        name: 'decodeBase64'
        value: 'true'
      }
      {
        name: 'ttlInSeconds'
        value: '0'
      }
    ]
    secrets: [
      {
        name: 'storage-account-key'
        value: storageAccount.listKeys().keys[0].value
      }
    ]
    scopes: [
      'daprsekiban-apiservice'
    ]
  }
}

// Output
output pubSubComponentName string = daprPubSubComponent.name
output queueBindingName string = daprQueueBinding.name