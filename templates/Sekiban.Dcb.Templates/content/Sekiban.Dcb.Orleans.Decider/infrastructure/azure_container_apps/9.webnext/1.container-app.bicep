param managedEnvName string = 'aca-${resourceGroup().name}'
param containerAppName string = 'webnext-${resourceGroup().name}'
param location string = resourceGroup().location
var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')
var containerImage string = '${acrName}.azurecr.io/${containerAppName}:latest'

module envModule 'env-module.bicep' = {
  name: 'envVarsModuleWebNext'
}

// Reference to existing Managed Environment
resource managedEnv 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: managedEnvName
}

// Get the Managed Environment ID
var managedEnvId = managedEnv.id

resource acr 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: acrName
}

// Create the basic Container Apps
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: managedEnvId
    configuration: {
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ]
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'webnext'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: envModule.outputs.envVars
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
