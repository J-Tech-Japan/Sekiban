// Apps-only Bicep file to deploy Container Apps for Sekiban Dapr Aspire
// This file should be deployed AFTER:
// 1. aca_infrastructure.bicep has been deployed
// 2. Container images have been pushed to ACR

targetScope = 'resourceGroup'

// Backend Container App with Dapr
module backendContainerApp '7.backend/2.container-app.bicep' = {
  name: 'backendContainerAppDeployment'
  params: {}
}

module backendKeyVaultAccess '7.backend/3.key-vault-access.bicep' = {
  name: 'backendKeyVaultAccessDeployment'
  params: {}
  dependsOn: [
    backendContainerApp
  ]
}

module backendDiagnosticSettings '7.backend/4.diagnostic-settings.bicep' = {
  name: 'backendDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    backendContainerApp
  ]
}

// EventRelay App Container with Dapr
module eventRelayContainerApp '9.eventrelay/1.container-app.bicep' = {
  name: 'eventRelayContainerAppDeployment'
  params: {
    serviceBusNamespace: 'sb-${resourceGroup().name}'
  }
}

module eventRelayDiagnosticSettings '9.eventrelay/2.diagnostic-settings.bicep' = {
  name: 'eventRelayDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    eventRelayContainerApp
  ]
}

// Frontend App Container
module blazorContainerApp '8.blazor/1.container-app.bicep' = {
  name: 'blazorContainerAppDeployment'
  params: {}
  dependsOn: [
    backendContainerApp
  ]
}

module blazorDiagnosticSettings '8.blazor/2.diagnositic-settings.bicep' = {
  name: 'blazorDiagnosticSettingsDeployment'
  params: {}
  dependsOn: [
    blazorContainerApp
  ]
}

// Outputs
output backendUrl string = backendContainerApp.outputs.url
output eventRelayUrl string = eventRelayContainerApp.outputs.url
output blazorUrl string = blazorContainerApp.outputs.url