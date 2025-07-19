param managedEnvName string = 'env-${resourceGroup().name}'
param containerAppName string = 'eventrelay-${resourceGroup().name}'
param location string = resourceGroup().location
param serviceBusNamespace string = 'sb-${resourceGroup().name}'
var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')
var containerImage string = '${acrName}.azurecr.io/${containerAppName}:latest'

// Get the service bus connection string
var serviceBusConnectionString = listKeys(resourceId('Microsoft.ServiceBus/namespaces/authorizationRules', serviceBusNamespace, 'RootManageSharedAccessKey'), '2022-01-01-preview').primaryConnectionString

module envModule 'env-module.bicep' = {
  name: 'envVarsModuleEventRelay'
  params: {}
}

// Reference to existing App Container Managed Environment
resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

// Get the App Container Managed Environment ID
var managedEnvId = managedEnv.id

// Reference to existing Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: acrName
}

// Create the EventRelay Container App with Dapr enabled
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: managedEnvId
    configuration: {
      secrets: concat(envModule.outputs.secretVars, [
        {
          name: 'servicebus-connectionstring'
          value: serviceBusConnectionString
        }
      ])
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      ingress: {
        external: false  // Internal only - no external access
        targetPort: 5020
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      dapr: {
        enabled: true
        appId: 'daprsekiban-eventrelay'
        appPort: 5020
        appProtocol: 'http'
        httpReadBufferSize: 4
        httpMaxRequestSize: 4
        logLevel: 'info'
        enableApiLogging: false
      }
    }
    template: {
      containers: [
        {
          name: 'eventrelay'
          image: containerImage
          env: envModule.outputs.envVars
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1  // Always keep at least one instance running
        maxReplicas: 5
        rules: [
          {
            name: 'servicebus-scaler'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                topicName: 'events.all'
                subscriptionName: 'event-relay-group'
                messageCount: '10'
              }
              auth: [
                {
                  secretRef: 'servicebus-connectionstring'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
          {
            name: 'cpu-scaler'
            custom: {
              type: 'cpu'
              metadata: {
                type: 'Utilization'
                value: '70'
              }
            }
          }
        ]
      }
    }
  }
}

output name string = containerApp.name
output url string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output containerAppResourceId string = containerApp.id
output principalId string = containerApp.identity.principalId
output tenantId string = subscription().tenantId