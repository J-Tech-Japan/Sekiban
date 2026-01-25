param managedEnvName string = 'aca-${resourceGroup().name}'
param containerAppName string = 'backend-${resourceGroup().name}'
param location string = resourceGroup().location
var acrName = replace(toLower('acr-${resourceGroup().name}'), '-', '')
var containerImage string = '${acrName}.azurecr.io/${containerAppName}:latest'

@description('The params provided by aca_main.bicep.')
param orleansQueueType string  = 'eventhub'
param orleansClusterType string = 'cosmos'
param orleansDefaultGrainType string = 'cosmos'

module envModule 'env-module.bicep' = {
  name: 'envVarsModule'
  params: {
    orleansQueueType: orleansQueueType
    orleansClusterType: orleansClusterType
    orleansDefaultGrainType: orleansDefaultGrainType
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

// Create the basic Container App
// Note: API version 2024-03-01 or later required for additionalPortMappings
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
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
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        // Additional TCP ports for Orleans silo-to-silo communication
        additionalPortMappings: [
          {
            targetPort: 11111  // Orleans Silo Port
            external: false    // Internal only within Container Apps environment
          }
          {
            targetPort: 30000  // Orleans Gateway Port
            external: false    // Internal only within Container Apps environment
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'backend'
          image: containerImage
          env: envModule.outputs.envVars
          // Orleans resources - adjust based on workload
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            // Startup probe: Wait for Orleans silo and ASP.NET Core to fully start
            // Readiness/Liveness probes won't run until this succeeds
            {
              type: 'Startup'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 30 // Allow up to 305 seconds (~5 minutes) for startup
              timeoutSeconds: 5
            }
            // Readiness probe: Check if ready to accept traffic
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 0 // Start immediately after Startup succeeds
              periodSeconds: 10
              timeoutSeconds: 5
              failureThreshold: 3
            }
            // Liveness probe: Check if application is still responsive
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 0 // Start immediately after Startup succeeds
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
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
