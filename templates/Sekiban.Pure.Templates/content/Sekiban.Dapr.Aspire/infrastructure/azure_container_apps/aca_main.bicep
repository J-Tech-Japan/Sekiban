// Main Bicep file to deploy all resources for Sekiban Dapr Aspire on Azure Container Apps
// This file orchestrates the deployment of various modules.

targetScope = 'resourceGroup'

@description('The type of Dapr state store to use')
@allowed(['azureblobstorage', 'azuretablestorage'])
param daprStateStoreType string = 'azureblobstorage'

@description('The type of Dapr pub/sub to use')
@allowed(['azurestoragequeues', 'azureservicebus'])
param daprPubSubType string = 'azurestoragequeues'

@secure()
param logAnalyticsSharedKey string

// 1. Key Vault
module keyVaultCreate '1.keyvault/create.bicep' = {
  name: 'keyVaultDeployment'
  params: {}
}

// 2. Storages (for Dapr State Store and Pub/Sub)
module storageCreate '2.storages/1.create.bicep' = {
  name: 'storageCreateDeployment'
  params: {}
}

module storageSaveKeyVault '2.storages/2.save-keyvault.bicep' = {
  name: 'storageSaveKeyVaultDeployment'
  params: {}
  dependsOn: [
    keyVaultCreate
    storageCreate
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
  params: {
    logAnalyticsSharedKey: logAnalyticsSharedKey
  }
  dependsOn: [
    storageCreate
    cosmosCreate
    appInsightsCreate
    vnetCreate
  ]
}

// 7. Dapr Components (deployed after managed environment)
module daprStateStoreComponent '6.dapr-components/1.statestore-component.bicep' = {
  name: 'daprStateStoreComponentDeployment'
  params: {
    daprStateStoreType: daprStateStoreType
  }
  dependsOn: [
    keyVaultCreate
    storageCreate
    storageSaveKeyVault
    managedEnv
  ]
}

module daprPubSubComponent '6.dapr-components/2.pubsub-component.bicep' = {
  name: 'daprPubSubComponentDeployment'
  params: {
    daprPubSubType: daprPubSubType
  }
  dependsOn: [
    keyVaultCreate
    storageCreate
    storageSaveKeyVault
    managedEnv
  ]
}

// 8. Backend App Container with Dapr
module backendContainerApp '7.backend/2.container-app.bicep' = {
  name: 'backendContainerAppDeployment'
  params: {
    daprStateStoreType: daprStateStoreType
    daprPubSubType: daprPubSubType
  }
  dependsOn: [
    managedEnv
    cosmosSaveKeyVault
    daprStateStoreComponent
    daprPubSubComponent
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