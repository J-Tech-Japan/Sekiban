@description('The type of Dapr pub/sub to use')
@allowed(['azurestoragequeues', 'azureservicebus'])
param daprPubSubType string = 'azureservicebus'

@description('The name of the existing managed environment')
param managedEnvironmentName string = 'env-${resourceGroup().name}'

// Reference existing resources
resource managedEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvironmentName
}

// This component is created by the pubsub-servicebus.bicep module
// We just need to output the component name for reference
output pubSubComponentName string = 'pubsub'