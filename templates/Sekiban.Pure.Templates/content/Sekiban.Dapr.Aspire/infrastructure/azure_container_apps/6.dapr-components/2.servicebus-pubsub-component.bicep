param managedEnvName string = 'env-${resourceGroup().name}'
param serviceBusNamespace string = 'sb-${resourceGroup().name}'

resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

// Get the service bus connection string
var serviceBusConnectionString = listKeys(resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', serviceBusNamespace, 'RootManageSharedAccessKey'), '2022-01-01-preview').primaryConnectionString

resource pubSubComponent 'Microsoft.App/managedEnvironments/daprComponents@2023-05-01' = {
  parent: managedEnv
  name: 'sekiban-pubsub'
  properties: {
    componentType: 'pubsub.azure.servicebus'
    version: 'v1'
    metadata: [
      {
        name: 'connectionString'
        secretRef: 'servicebus-connectionstring'
      }
      {
        name: 'maxConcurrentHandlers'
        value: '32'
      }
      {
        name: 'enableEntityManagement'
        value: 'true'
      }
    ]
    scopes: [
      'daprsekiban-apiservice'
      'daprsekiban-eventrelay'
    ]
    secrets: [
      {
        name: 'servicebus-connectionstring'
        value: serviceBusConnectionString
      }
    ]
  }
}

output componentName string = pubSubComponent.name