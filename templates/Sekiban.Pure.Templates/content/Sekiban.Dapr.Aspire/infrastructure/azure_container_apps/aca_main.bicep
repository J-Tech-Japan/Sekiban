// Main Bicep file to deploy all resources for Sekiban Dapr Aspire on Azure Container Apps
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

@description('Cosmos DB account name for Dapr state store')
param cosmosAccountName string = 'cosmos-${resourceGroup().name}'

@description('Service Bus namespace for Dapr pub/sub')
param serviceBusNamespace string = 'sb-${resourceGroup().name}'

// Remove the logAnalyticsSharedKey parameter as it will be retrieved within the module

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Service Bus (for Dapr Pub/Sub)
module serviceBusCreate '2.servicebus/1.create.bicep' = {
  name: 'serviceBusCreateDeployment'
  params: {
    serviceBusNamespace: serviceBusNamespace
  }
}

module servicebusSaveKeyVault '2.servicebus/2.save-keyvault.bicep' = {
  name: 'servicebusSaveKeyVaultDeployment'
  params: {
    serviceBusNamespace: serviceBusNamespace
  }
  dependsOn: [
    keyVaultCreate
    serviceBusCreate
  ]
}

// 3. Cosmos DB (for Sekiban Event Store)
module cosmosCreate '3.cosmos/1.create.bicep' = {
  name: 'cosmosCreateDeployment'
  params: {}
}

module cosmosDatabase '3.cosmos/2.database.bicep' = {
  name: 'cosmosDatabaseDeployment'
  params: {}
  dependsOn: [
    cosmosCreate
  ]
}

module cosmosContainer '3.cosmos/3.container.bicep' = {
  name: 'cosmosContainerDeployment'
  params: {}
  dependsOn: [
    cosmosDatabase
  ]
}

module cosmosDaprDatabase '3.cosmos/5.dapr-database.bicep' = {
  name: 'cosmosDaprDatabaseDeployment'
  params: {}
  dependsOn: [
    cosmosCreate
  ]
}

module cosmosDaprContainer '3.cosmos/6.dapr-container.bicep' = {
  name: 'cosmosDaprContainerDeployment'
  params: {}
  dependsOn: [
    cosmosDaprDatabase
  ]
}

module cosmosSaveKeyVault '3.cosmos/4.save-keyvault.bicep' = {
  name: 'cosmosSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    cosmosCreate
  ]
}

// 4. VNet
module vnetCreate '4.vnet/1.create.bicep' = {
  name: 'vnetCreateDeployment'
  params: {}
}

// 5. Application Insights & Log Analytics
module appInsightsCreate '5.applicationinsights/1.application-insights.bicep' = {
  name: 'appInsightsCreateDeployment'
  params: {}
}

// 6. Managed Environment (must be created before Dapr components)
module managedEnv '7.backend/1.managed-env.bicep' = {
  name: 'managedEnvDeployment'
  params: {}
  dependsOn: [
    serviceBusCreate
    cosmosCreate
    appInsightsCreate
    vnetCreate
  ]
}

// 7. Dapr Components (deployed after managed environment)
module daprCosmosStateStoreComponent '6.dapr-components/1.cosmos-statestore-component.bicep' = {
  name: 'daprCosmosStateStoreComponentDeployment'
  params: {
    cosmosAccountName: cosmosAccountName
  }
  dependsOn: [
    keyVaultCreate
    cosmosCreate
    cosmosSaveKeyVault
    cosmosDaprContainer
    managedEnv
  ]
}

module daprServiceBusPubSubComponent '6.dapr-components/2.servicebus-pubsub-component.bicep' = {
  name: 'daprServiceBusPubSubComponentDeployment'
  params: {
    serviceBusNamespace: serviceBusNamespace
  }
  dependsOn: [
    keyVaultCreate
    serviceBusCreate
    servicebusSaveKeyVault
    managedEnv
  ]
}

// 8. Backend App Container with Dapr
module backendContainerApp '7.backend/2.container-app.bicep' = {
  name: 'backendContainerAppDeployment'
  params: {
    cosmosAccountName: cosmosAccountName
    serviceBusNamespace: serviceBusNamespace
  }
  dependsOn: [
    managedEnv
    cosmosSaveKeyVault
    servicebusSaveKeyVault
    daprCosmosStateStoreComponent
    daprServiceBusPubSubComponent
  ]
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

// 9. Frontend App Container
module blazorContainerApp '8.blazor/1.container-app.bicep' = {
  name: 'blazorContainerAppDeployment'
  params: {}
  dependsOn: [
    appInsightsCreate
    vnetCreate
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
output blazorUrl string = blazorContainerApp.outputs.url
output keyVaultName string = keyVaultCreate.outputs.keyVaultName
output managedEnvironmentId string = managedEnv.outputs.id