param managedEnvName string = 'env-${resourceGroup().name}'
param serviceBusNamespace string = 'sb-${resourceGroup().name}'

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

resource pubSubComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnv
  name: 'pubsub'
  properties: {
    componentType: 'pubsub.azure.servicebus.queues'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'servicebus-connectionstring'
      }
      {
        name: 'queueName'
        value: 'daprpubsub'
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
    secrets: [
      {
        name: 'servicebus-connectionstring'
        keyVaultUrl: 'https://kv-${resourceGroup().name}${environment().suffixes.keyvaultDns}/secrets/ServiceBusConnectionString'
        identity: 'system'
      }
    ]
  }
}

output componentName string = pubSubComponent.name