param managedEnvName string = 'env-${resourceGroup().name}'
param serviceBusNamespaceName string = 'sb-${resourceGroup().name}'
param serviceBusQueueName string = 'daprpubsub'

// Reference to existing Managed Environment
resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

// Reference to existing Service Bus Namespace
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

// Create Service Bus Queue
resource serviceBusQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: serviceBusQueueName
  properties: {
    lockDuration: 'PT1M'
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    requiresSession: false
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
    maxDeliveryCount: 10
  }
}

// Get Service Bus connection string
var serviceBusConnectionString = serviceBusNamespace.listKeys().primaryConnectionString

// Create Dapr Component for PubSub
resource daprPubSubComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnv
  name: 'pubsub'
  properties: {
    componentType: 'pubsub.azure.servicebus.queues'
    version: 'v1'
    ignoreErrors: false
    initTimeout: '30s'
    secrets: [
      {
        name: 'servicebus-connectionstring'
        value: serviceBusConnectionString
      }
    ]
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'servicebus-connectionstring'
      }
      {
        name: 'queueName'
        value: serviceBusQueueName
      }
      {
        name: 'maxActiveMessages'
        value: '100'
      }
      {
        name: 'maxConcurrentHandlers'
        value: '10'
      }
      {
        name: 'lockDurationInSec'
        value: '60'
      }
      {
        name: 'autoDeleteOnIdleInSec'
        value: '0'
      }
      {
        name: 'defaultMessageTimeToLiveInSec'
        value: '604800'
      }
    ]
    scopes: [
      'daprsekiban-apiservice'
    ]
  }
}

output componentName string = daprPubSubComponent.name