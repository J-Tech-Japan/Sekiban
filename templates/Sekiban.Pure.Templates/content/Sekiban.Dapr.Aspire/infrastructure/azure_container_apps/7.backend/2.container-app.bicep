param managedEnvName string = 'env-${resourceGroup().name}'
param containerAppName string = 'backend-${resourceGroup().name}'
param location string = resourceGroup().location
var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')
var containerImage string = '${acrName}.azurecr.io/${containerAppName}:latest'

@description('The params provided by aca_main.bicep.')
param cosmosAccountName string = 'cosmos-${resourceGroup().name}'
param serviceBusNamespace string = 'sb-${resourceGroup().name}'

module envModule 'env-module.bicep' = {
  name: 'envVarsModule'
  params: {
    cosmosAccountName: cosmosAccountName
    serviceBusNamespace: serviceBusNamespace
  }
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

// Create the Container App with Dapr enabled
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: managedEnvId
    configuration: {
      secrets: envModule.outputs.secretVars
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      ingress: {
        external: false
        targetPort: 8080
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
        appId: 'daprsekiban-apiservice'
        appPort: 8080
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
          name: 'backend'
          image: containerImage
          env: envModule.outputs.envVars
          // Default value: 0.5/1Gi
          // resources: {
          //   cpu: 1
          //   memory: '2Gi'
          // }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
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